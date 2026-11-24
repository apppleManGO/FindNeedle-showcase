using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

// The seconds after the needle comes up: the finder lifts it, the camera draws back, then the result panel.
// In co-op the others' cameras turn to the finder. Messages are handed over directly; no network is involved.
public class NeedleMomentTests
{
    static BeachSearch Beach()
    {
        var game = Object.FindFirstObjectByType<BeachSearch>();
        Assert.IsNotNull(game);
        game.Visit(game.Theme);
        Assert.IsTrue(game.Started);
        return game;
    }

    // Winning saves a best time and unlocks the beaches. These tests win, so they put the saved values back.
    static readonly string[] Saved = {"FindNeedle.Best.Neighborhood","FindNeedle.Best.Tournament","FindNeedle.Best.Festival","FindNeedle.ThemeChoice"};
    static string[] KeepRecords() => System.Array.ConvertAll(Saved,key=>PlayerPrefs.HasKey(key) ? key : null);
    static float[] KeepValues() => System.Array.ConvertAll(Saved,key=>key.EndsWith("ThemeChoice") ? PlayerPrefs.GetInt(key,0) : PlayerPrefs.GetFloat(key,0));
    static void PutBack(string[] present,float[] values)
    {
        for (int i=0; i<Saved.Length; i++)
        {
            if (present[i]==null) PlayerPrefs.DeleteKey(Saved[i]);
            else if (Saved[i].EndsWith("ThemeChoice")) PlayerPrefs.SetInt(Saved[i],(int)values[i]);
            else PlayerPrefs.SetFloat(Saved[i],values[i]);
        }
        PlayerPrefs.Save();
    }

    static IEnumerator Until(float seconds)
    {
        float end = Time.time+seconds;
        while (Time.time<end) yield return null;
    }

    [UnityTest] public IEnumerator TheFinderLiftsTheNeedleBeforeTheResult()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game = Beach();
        var present = KeepRecords();
        var values = KeepValues();
        var moment = game.Moment;
        var view = game.player.view.transform;

        game.Win();
        Assert.IsTrue(game.Won);
        Assert.AreEqual(game.MyId,game.Finder);
        Assert.IsTrue(moment.Playing,"the result came without the moment");
        Assert.IsFalse(game.player.Presenting,"the camera left first person at once");

        yield return Until(.8f);
        var lifted = view.Find("찾은 바늘");
        Assert.IsNotNull(lifted,"the needle is not in the finder's view");
        Assert.Less(Mathf.Abs(lifted.localPosition.x),.1f,"the needle is not coming up to the middle");
        Assert.Greater(moment.Vignette,.5f);

        yield return Until(NeedleMoment.Reveal+.3f-moment.Elapsed);
        Assert.IsTrue(game.player.Presenting,"no third-person shot after the lift");
        Assert.IsNull(view.Find("찾은 바늘"));
        var overhead = game.player.transform.Find("찾은 바늘");
        Assert.IsNotNull(overhead,"the figure is not holding the needle up");
        Assert.Greater(overhead.position.y,game.player.transform.position.y+1.2f,"the needle is not overhead");

        yield return Until(NeedleMoment.End+.1f-moment.Elapsed);
        Assert.IsFalse(moment.Playing,"the moment never ended");
        Assert.AreEqual(0f,moment.Vignette);
        PutBack(present,values);
        yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator TheWinSaysWhoFoundIt()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game = Beach();
        var present = KeepRecords();
        var values = KeepValues();
        var sent = new List<byte[]>();
        game.MyId = 2;
        game.Guest = true;
        game.Outbox = sent.Add;
        game.Win();
        byte[] win = null;
        foreach (var message in sent) if (message[0]==(byte)BeachSearch.Op.Win) win = message;
        Assert.IsNotNull(win);
        Assert.AreEqual(2,win[win.Length-1],"the win does not name its finder");
        game.Outbox = null;
        game.Guest = false;
        PutBack(present,values);
        yield return new ExitPlayMode();
    }

    // Lambdas inside a test that enters play mode lose what they capture when Unity reloads the scripts,
    // so the session's hooks are made out here.
    static void Ignore(byte[] message) { }
    static System.Func<IReadOnlyDictionary<int,BeachGhost>> Fixed(Dictionary<int,BeachGhost> team) => () => team;

    [UnityTest] public IEnumerator TeammatesTurnToTheFinder()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game = Beach();
        var present = KeepRecords();
        var values = KeepValues();
        var me = game.player.transform;
        // The host stands a few metres off to the side, out of this player's view.
        Vector3 there = me.position+me.right*3f+me.forward*.5f;
        there.y = game.sand.Height(new Vector2(there.x,there.z));
        var host = BeachGhost.Create(game.transform,0,there);
        var mate = BeachGhost.Create(game.transform,3,there+Vector3.right);
        var team = new Dictionary<int,BeachGhost>{{0,host},{3,mate}};
        game.MyId = 1;
        game.Guest = true;
        game.Outbox = Ignore;
        game.Team = Fixed(team);
        yield return null;

        using (var stream = new MemoryStream())
        using (var w = new BinaryWriter(stream))
        {
            w.Write((byte)BeachSearch.Op.Win);
            w.Write(75f);
            w.Write((byte)0);
            w.Flush();
            game.Receive(stream.ToArray(),0);
        }
        Assert.IsTrue(game.Won);
        Assert.AreEqual(0,game.Finder);
        Assert.IsFalse(game.Moment.FoundHere);
        Assert.IsNull(game.player.view.transform.Find("찾은 바늘"),"the needle came up in the wrong player's hands");

        yield return Until(1.1f);
        var view = game.player.view.transform;
        float off = Vector3.Angle(view.forward,host.transform.position+Vector3.up*1.1f-view.position);
        Assert.Less(off,12f,"the camera did not turn to the finder");

        yield return Until(NeedleMoment.Reveal+.4f-game.Moment.Elapsed);
        Assert.IsTrue(host.Cheering);
        Assert.IsTrue(mate.Cheering,"the rest of the team does not cheer");
        Assert.IsNotNull(host.transform.Find("찾은 바늘"),"the finder is not holding the needle up");
        Assert.IsNull(me.Find("찾은 바늘"),"this player holds a needle they did not find");
        float facing = Vector3.Angle(view.forward,host.transform.position+Vector3.up*1.1f-view.position);
        Assert.Less(facing,15f,"the shot is not on the finder");

        game.SessionEnded();
        game.Outbox = null;
        game.Guest = false;
        PutBack(present,values);
        yield return new ExitPlayMode();
    }
}
