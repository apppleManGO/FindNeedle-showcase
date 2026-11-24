using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

// What one player sees of the others: what they hold, and the one volleyball everybody shares.
// Messages are handed over directly, as in BeachSessionTests; no network is involved.
public class BeachTeamViewTests
{
    [Test] public void EverySlotSurvivesTheWire()
    {
        foreach (BeachGear gear in System.Enum.GetValues(typeof(BeachGear)))
            Assert.AreEqual(new BeachSlot(gear),BeachSlot.FromCode(new BeachSlot(gear).Code));
        foreach (FindKind kind in System.Enum.GetValues(typeof(FindKind)))
            Assert.AreEqual(new BeachSlot(kind),BeachSlot.FromCode(new BeachSlot(kind).Code));
    }

    // EnterPlayMode works only when yielded by the test itself, so each test opens the scene first.
    static void OpenScene() => EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");

    static BeachSearch Beach()
    {
        var game = Object.FindFirstObjectByType<BeachSearch>();
        Assert.IsNotNull(game);
        game.Visit(game.Theme);
        Assert.IsTrue(game.Started);
        return game;
    }

    static List<BeachSearch.Op> Kinds(List<byte[]> messages) => messages.ConvertAll(m=>(BeachSearch.Op)m[0]);

    [UnityTest] public IEnumerator ATeammatesToolAndSandShowOnTheirFigure()
    {
        OpenScene();
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game = Beach();
        var ghost = BeachGhost.Create(game.transform,1,game.player.transform.position+Vector3.right*2);
        yield return null;

        ghost.Hold(new BeachSlot(BeachGear.Trowel),true,true,game);
        yield return null;
        var held = ghost.transform.Find("든 것 · 모종삽");
        Assert.IsNotNull(held,"the trowel is not in their hand");
        Assert.IsTrue(held.gameObject.activeSelf);
        Assert.Less(Vector3.Distance(held.position,ghost.transform.position),1.6f,"the trowel floats away from them");
        var sand = ghost.transform.Find("든 모래");
        Assert.IsNotNull(sand,"the sand on the blade is missing");
        Assert.IsTrue(sand.gameObject.activeSelf);

        ghost.Hold(new BeachSlot(BeachGear.Detector),false,false,game);
        yield return null;
        Assert.IsNull(ghost.transform.Find("든 것 · 모종삽"),"the old tool stayed in hand");
        Assert.IsNotNull(ghost.transform.Find("든 것 · 탐지기"));
        Assert.IsFalse(sand.gameObject.activeSelf,"sand still shows after it was set down");

        ghost.Hold(new BeachSlot(BeachGear.Hands),false,false,game);
        yield return null;
        Assert.IsNull(ghost.transform.Find("든 것 · 탐지기"));
        yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator TheHostSettlesWhoHasTheBall()
    {
        OpenScene();
        yield return new EnterPlayMode();
        // The court stands only on the tournament beach.
        BeachSearch.FollowHost(BeachTheme.Tournament);
        yield return null;
        yield return null;
        var game = Object.FindFirstObjectByType<BeachSearch>();
        Assert.AreEqual(BeachTheme.Tournament,game.Theme);
        if (!game.Started) game.Begin();
        var ball = game.Volleyball;
        Assert.IsNotNull(ball);
        var sent = new List<byte[]>();
        game.MyId = 0;
        game.Guest = false;
        game.Outbox = sent.Add;

        // Player 1 asks, and gets it; player 2 asks too late.
        game.Receive(new[]{(byte)BeachSearch.Op.BallRequest},1);
        Assert.AreEqual(1,ball.Holder);
        Assert.IsFalse(ball.Model.gameObject.activeSelf,"the host should see the ball in player 1's figure, not its own");
        Assert.Contains(BeachSearch.Op.BallHolder,Kinds(sent));
        game.Receive(new[]{(byte)BeachSearch.Op.BallRequest},2);
        Assert.AreEqual(1,ball.Holder);

        // Only the holder can throw it.
        game.Receive(Throw(new Vector3(0,1,0),Vector3.forward),2);
        Assert.AreEqual(1,ball.Holder);
        game.Receive(Throw(new Vector3(0,1,0),Vector3.forward),1);
        Assert.AreEqual(-1,ball.Holder);
        Assert.IsTrue(ball.Model.gameObject.activeSelf);

        // The host tells the others where it is as it flies.
        sent.Clear();
        for (int frame=0; frame<10; frame++) yield return null;
        Assert.Contains(BeachSearch.Op.BallState,Kinds(sent));

        // A holder who leaves sends the ball back to the court.
        game.Receive(new[]{(byte)BeachSearch.Op.BallRequest},2);
        Assert.AreEqual(2,ball.Holder);
        game.PlayerLeft(2);
        Assert.AreEqual(-1,ball.Holder);
        game.Outbox = null;
        yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator AGuestCatchesOnlyWhenTheHostSaysSo()
    {
        OpenScene();
        yield return new EnterPlayMode();
        // The court stands only on the tournament beach.
        BeachSearch.FollowHost(BeachTheme.Tournament);
        yield return null;
        yield return null;
        var game = Object.FindFirstObjectByType<BeachSearch>();
        Assert.AreEqual(BeachTheme.Tournament,game.Theme);
        if (!game.Started) game.Begin();
        var ball = game.Volleyball;
        var sent = new List<byte[]>();
        game.MyId = 1;
        game.Guest = true;
        game.Outbox = sent.Add;

        Assert.IsFalse(ball.TryCatch(),"a guest must not take the ball by itself");
        Assert.Contains(BeachSearch.Op.BallRequest,Kinds(sent));
        Assert.IsFalse(game.Local.HoldingBall);

        // Someone else got it first.
        game.Receive(new[]{(byte)BeachSearch.Op.BallHolder,(byte)2},0);
        Assert.AreEqual(2,ball.Holder);
        Assert.IsFalse(game.Local.HoldingBall);

        // Then it is this player's.
        game.Receive(State(Vector3.one,-1),0);
        Assert.AreEqual(-1,ball.Holder);
        game.Receive(new[]{(byte)BeachSearch.Op.BallHolder,(byte)1},0);
        Assert.IsTrue(game.Local.HoldingBall);
        Assert.AreEqual(BeachGear.Ball,game.Local.InHand.Gear);
        // A word sent before the catch must not take it away again.
        game.Receive(State(Vector3.one,-1),0);
        Assert.IsTrue(game.Local.HoldingBall);

        // Throwing tells the host instead of flying it here.
        sent.Clear();
        ball.Throw(Vector3.forward);
        Assert.IsFalse(game.Local.HoldingBall);
        Assert.Contains(BeachSearch.Op.BallThrow,Kinds(sent));

        // Being hit is the host's call.
        int before = game.player.Health;
        game.Receive(new[]{(byte)BeachSearch.Op.BallHit,(byte)2},0);
        Assert.AreEqual(before,game.player.Health);
        game.Receive(new[]{(byte)BeachSearch.Op.BallHit,(byte)1},0);
        Assert.Less(game.player.Health,before);

        // Leaving makes the beach a solo one again, with the ball back on the court.
        game.Receive(new[]{(byte)BeachSearch.Op.BallHolder,(byte)2},0);
        game.SessionEnded();
        game.Outbox = null;
        game.Guest = false;
        Assert.AreEqual(-1,ball.Holder);
        Assert.IsTrue(ball.Model.gameObject.activeSelf);
        yield return new ExitPlayMode();
    }

    static byte[] Throw(Vector3 from,Vector3 direction)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write((byte)BeachSearch.Op.BallThrow);
        w.Write(from);
        w.Write(direction);
        w.Flush();
        return stream.ToArray();
    }

    static byte[] State(Vector3 position,int holder)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write((byte)BeachSearch.Op.BallState);
        w.Write(position);
        w.Write((sbyte)holder);
        w.Flush();
        return stream.ToArray();
    }
}
