using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

// Signs from the emote wheel or keys 1-8: the player's own figure makes them, the message carries them, and
// teammates' figures make them where everyone can see, for a moment.
public class BeachGestureTests
{
    static BeachSearch Beach()
    {
        var game = Object.FindFirstObjectByType<BeachSearch>();
        Assert.IsNotNull(game);
        game.Visit(game.Theme);
        Assert.IsTrue(game.Started);
        return game;
    }

    static readonly List<byte[]> sent = new();
    static void Collect(byte[] message) => sent.Add(message);
    static Dictionary<int,BeachGhost> team;
    static IReadOnlyDictionary<int,BeachGhost> Team() => team;

    static Transform frame;
    static float until;
    static Vector3 Local(Vector3 world) => frame.InverseTransformPoint(world);

    [Test] public void EachSignSaysSomething()
    {
        foreach (BeachBody.Gesture sign in System.Enum.GetValues(typeof(BeachBody.Gesture)))
        {
            if (sign==BeachBody.Gesture.None) Assert.IsNull(BeachBody.Said(sign));
            else Assert.IsFalse(string.IsNullOrEmpty(BeachBody.Said(sign)),sign+" has no words");
        }
    }

    [Test] public void TheWheelHasEightDifferentSigns()
    {
        Assert.AreEqual(8,BeachBody.Wheel.Length);
        CollectionAssert.AllItemsAreUnique(BeachBody.Wheel);
        foreach (var sign in BeachBody.Wheel) Assert.IsTrue(BeachBody.Known(sign),sign+" is not a sign");
        Assert.IsFalse(BeachBody.Known(BeachBody.Gesture.None));
        Assert.IsFalse(BeachBody.Known((BeachBody.Gesture)99));
    }

    [Test] public void TheWheelMakesTheSignThePointerIsOver()
    {
        var wheel = new BeachEmoteWheel();
        Assert.AreEqual(BeachBody.Gesture.None,wheel.Release(),"a closed wheel chose something");
        for (int i=0; i<BeachBody.Wheel.Length; i++)
        {
            wheel.Show();
            Assert.AreEqual(-1,wheel.Hovered,"a fresh wheel should point at nothing");
            // The mouse reports y up; the wheel's layout runs y down.
            Vector2 toward = BeachEmoteWheel.Direction(i);
            wheel.Move(new Vector2(toward.x,-toward.y)*400);
            Assert.AreEqual(i,wheel.Hovered,"moving toward segment "+i);
            Assert.LessOrEqual(wheel.Pointer.magnitude,BeachEmoteWheel.Outer,"the pointer left the wheel");
            Assert.AreEqual(BeachBody.Wheel[i],wheel.Release());
            Assert.IsFalse(wheel.Open);
        }
        // Up is the first sign, right the third, a little past a boundary the next one.
        Assert.AreEqual(0,BeachEmoteWheel.Segment(new Vector2(0,-1)));
        Assert.AreEqual(2,BeachEmoteWheel.Segment(new Vector2(1,0)));
        Assert.AreEqual(4,BeachEmoteWheel.Segment(new Vector2(0,1)));
        Assert.AreEqual(6,BeachEmoteWheel.Segment(new Vector2(-1,0)));
        Assert.AreEqual(1,BeachEmoteWheel.Segment(new Vector2(Mathf.Sin(24*Mathf.Deg2Rad),-Mathf.Cos(24*Mathf.Deg2Rad))));

        // Let go near the middle: nothing.
        wheel.Show();
        wheel.Move(new Vector2(20,10));
        Assert.AreEqual(-1,wheel.Hovered);
        Assert.AreEqual(BeachBody.Gesture.None,wheel.Release());
        // Escape and the like: nothing either.
        wheel.Show();
        wheel.Move(new Vector2(400,0));
        wheel.Cancel();
        Assert.AreEqual(BeachBody.Gesture.None,wheel.Release());
    }

    [UnityTest] public IEnumerator ASignIsSeenByTheTeamAndPasses()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game = Beach();
        Assert.IsNotNull(game.player.Body);

        // My own figure makes the sign, and it goes out to the others.
        sent.Clear();
        game.Outbox = Collect;
        game.Sign(BeachBody.Gesture.Wave);
        game.Outbox = null;
        Assert.AreEqual(BeachBody.Gesture.Wave,game.player.Body.Showing);
        Assert.AreEqual(1,sent.Count);
        Assert.AreEqual((byte)BeachSearch.Op.Gesture,sent[0][0]);

        // A teammate's sign arrives: their figure makes it, and the host passes it on.
        var ghost = BeachGhost.Create(game.transform,1,game.Surface(new Vector2(.5f,0)));
        team = new Dictionary<int,BeachGhost> { [1] = ghost };
        game.Team = Team;
        yield return null;
        Assert.IsNotNull(ghost.Body);
        Assert.IsTrue(game.Receive(sent[0],1),"the host should pass a sign on");
        Assert.AreEqual(BeachBody.Gesture.Wave,ghost.Body.Showing);

        // A newer sign replaces it, and nonsense is ignored.
        Assert.IsTrue(game.Receive(new[]{(byte)BeachSearch.Op.Gesture,(byte)BeachBody.Gesture.No},1));
        Assert.AreEqual(BeachBody.Gesture.No,ghost.Body.Showing);
        game.Receive(new[]{(byte)BeachSearch.Op.Gesture,(byte)99},1);
        Assert.AreEqual(BeachBody.Gesture.No,ghost.Body.Showing);
        // Someone not on the beach: nothing happens.
        game.Receive(new[]{(byte)BeachSearch.Op.Gesture,(byte)BeachBody.Gesture.Cheer},7);

        for (until = Time.time+BeachBody.GestureSeconds+.15f; Time.time<until;) yield return null;
        Assert.AreEqual(BeachBody.Gesture.None,ghost.Body.Showing,"the sign should not stay");
        Assert.AreEqual(BeachBody.Gesture.None,game.player.Body.Showing);
        game.Team = null;
        Object.Destroy(ghost.gameObject);
        yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator TheArmsShowTheSign()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game = Beach();
        var ghost = BeachGhost.Create(game.transform,2,game.Surface(new Vector2(-.5f,.2f)));
        yield return null;
        yield return null;
        var body = ghost.Body;
        Assert.IsNotNull(body);
        Assert.IsNotNull(body.RightHand,"this figure has no arm bones to sign with");
        var head = body.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Head);
        Assert.IsNotNull(head);
        frame = ghost.transform;
        float idleHand = Local(body.RightHand.position).y;
        Assert.Less(idleHand,Local(head.position).y,"the hand was already above the head");

        // Waving: the right hand goes up above the head.
        body.Sign(BeachBody.Gesture.Wave);
        for (until = Time.time+.5f; Time.time<until;) yield return null;
        Assert.Greater(Local(body.RightHand.position).y,Local(head.position).y,"the waving hand is not raised");

        // Pointing: the right hand reaches out in front.
        body.Sign(BeachBody.Gesture.Point);
        for (until = Time.time+.5f; Time.time<until;) yield return null;
        Vector3 pointing = Local(body.RightHand.position);
        Assert.Greater(pointing.z,.35f,"the pointing hand is not out in front");
        Assert.Greater(pointing.y,idleHand+.2f);

        // Once it is over, the arm comes back down.
        for (until = Time.time+BeachBody.GestureSeconds; Time.time<until;) yield return null;
        Assert.Less(Mathf.Abs(Local(body.RightHand.position).y-idleHand),.12f,"the arm stayed up");

        // Thanks: a bow brings the head down and forward.
        Vector3 upright = Local(head.position);
        body.Sign(BeachBody.Gesture.Thanks);
        for (until = Time.time+.6f; Time.time<until;) yield return null;
        Vector3 bowed = Local(head.position);
        Assert.Less(bowed.y,upright.y-.05f,"the head did not come down");
        Assert.Greater(bowed.z,upright.z+.05f,"the head did not go forward");

        // Come here: the hand ends up higher than it hangs, held out in front.
        for (until = Time.time+BeachBody.GestureSeconds; Time.time<until;) yield return null;
        body.Sign(BeachBody.Gesture.Come);
        for (until = Time.time+.5f; Time.time<until;) yield return null;
        Assert.Greater(Local(body.RightHand.position).z,.2f,"the beckoning hand is not out in front");
        Assert.Greater(Local(body.RightHand.position).y,idleHand+.25f);
        Object.Destroy(ghost.gameObject);
        yield return new ExitPlayMode();
    }
}
