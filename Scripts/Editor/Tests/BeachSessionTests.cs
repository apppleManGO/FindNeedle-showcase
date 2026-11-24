using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// What a second player would see. One beach is played while its outgoing messages are kept; the scene is
// then loaded afresh, given the first beach's snapshot and fed the same messages, as a guest's machine
// would be. Both must end up the same. No network is involved, and the needle is never taken.
public class BeachSessionTests
{
    static byte[] start;
    static readonly List<byte[]> sent = new();
    static float[] heights;
    static bool[] active;
    static Vector3[] places;
    static int picked;

    static BeachSearch Beach()
    {
        var game = Object.FindFirstObjectByType<BeachSearch>();
        Assert.IsNotNull(game);
        game.Visit(game.Theme);
        Assert.IsTrue(game.Started);
        return game;
    }

    [UnityTest] public IEnumerator AGuestFedTheSameMessagesSeesTheSameBeach()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;

        var game = Beach();
        var me = game.Local;
        start = game.WriteSnapshot();
        sent.Clear();
        game.MyId = 0;
        game.Outbox = sent.Add;

        // A handful with the coin in it, set down elsewhere, and the coin picked up.
        var coin = System.Array.Find(game.finds,item=>item.kind==FindKind.Coin);
        Vector2 hole = coin.Position;
        me.HandScoop(hole);
        me.HandScoop(game.sand.ClampToBeach(hole+new Vector2(.9f,0),.5f));
        me.Take(coin);
        Assert.AreEqual(1,game.Picked);
        // A sweep, some footprints, a parasol, and a hole deep enough that its sides slide.
        Vector2 sweep = game.sand.ClampToBeach(hole+new Vector2(-1.2f,.4f),.6f);
        for (int step=0; step<5; step++) game.SweepStep(sweep,Vector2.right,Mathf.Lerp(.035f,.29f,step/5f),step==0);
        game.SurfaceWorked();
        for (int step=0; step<4; step++) game.Footstep(sweep+new Vector2(.3f*step,-1f),Vector2.right);
        Vector2 pit = game.sand.ClampToBeach(hole+new Vector2(.2f,1.6f),.8f);
        var scrap = new List<BeachFind>();
        for (int i=0; i<4; i++) { scrap.Clear(); game.ScoopSand(pit,.45f,.14f,scrap,true,Vector2.up); }
        game.FinishStroke(pit,.45f,true);
        Vector2 shade = game.sand.ClampToBeach(pit+new Vector2(1.5f,1.2f),.6f);
        if (game.CanPitchParasol(shade)) game.PitchParasol(shade);
        var bottle = System.Array.Find(game.finds,item=>item.kind==FindKind.PlasticBottle);
        Vector2 pull = (Vector2.zero-bottle.Position).normalized;
        game.Rake(bottle.Position,pull);
        game.AddEarnings(4,2);
        game.Outbox = null;
        Assert.Greater(sent.Count,10,"the beach announced too little");

        for (int frame=0; frame<240; frame++) yield return null;
        heights = (float[])game.sand.Heights.Clone();
        active = System.Array.ConvertAll(game.finds,item=>item.gameObject.activeSelf);
        places = System.Array.ConvertAll(game.finds,item=>item.transform.position);
        picked = game.Picked;
        bool pitched = game.ParasolPitched;

        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        yield return null;
        yield return null;
        var guest = Beach();
        Assert.AreNotSame(game,guest);
        Assert.IsTrue(guest.ReadSnapshot(start),"the snapshot was refused");
        // Someone else's machine: the coin was picked up by player 0, not by this one.
        guest.Guest = true;
        guest.MyId = 1;
        foreach (var message in sent) guest.Receive(message,0);
        for (int frame=0; frame<240; frame++) yield return null;

        Assert.AreEqual(2,guest.Sorted);
        Assert.AreEqual(4,guest.Earned);
        Assert.AreEqual(picked,guest.Picked);
        Assert.AreEqual(0,guest.inventory.Count,"the guest must not receive the host's coin");
        Assert.AreEqual(pitched,guest.ParasolPitched);
        for (int i=0; i<active.Length; i++)
        {
            Assert.AreEqual(active[i],guest.finds[i].gameObject.activeSelf,"find "+i+" "+guest.finds[i].kind);
            if (active[i]) Assert.Less(Vector3.Distance(places[i],guest.finds[i].transform.position),.002f,"find "+i+" lies elsewhere");
        }
        float worst = 0;
        for (int i=0; i<heights.Length; i++) worst = Mathf.Max(worst,Mathf.Abs(heights[i]-guest.sand.Heights[i]));
        Assert.Less(worst,1e-4f,"the sand differs");

        yield return new ExitPlayMode();
    }
}
