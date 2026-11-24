using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

// Plays the real scene for a moment: a visit starts, a handful of sand is lifted with the coin in it,
// set down again, and the coin is picked up. It never takes the needle, so saved records stay untouched.
public class BeachPlaySmokeTests
{
    static byte[] lastSent;

    [TestCase("HandRake","RakeFinish",.67f)]
    [TestCase("SorterMagnet","MagnetFinish",.135f)]
    [TestCase("Bottle","FoodFinish",.244f)]
    [TestCase("Wrapper","FoodFinish",.211f)]
    [TestCase("Toothpick","LitterFinish",.26f)]
    [TestCase("CigaretteButt","LitterFinish",.069f)]
    [TestCase("Straw","LitterFinish",.20f)]
    [TestCase("FoamChunk","LitterFinish",.13f)]
    [TestCase("PlasticBag","LitterFinish",.22f)]
    [TestCase("RopeScrap","LitterFinish",.26f)]
    [TestCase("FishingNet","LitterFinish",.99f)]
    public void DownloadedPropImportsAtGameplayScale(string model,string finish,float longestSide)
    {
        var prop = Resources.Load<GameObject>("BeachProps/"+model);
        Assert.IsNotNull(prop);
        var renderer = prop.GetComponentInChildren<MeshRenderer>();
        var size = renderer.bounds.size;
        Assert.That(Mathf.Max(size.x,size.y,size.z),Is.EqualTo(longestSide).Within(.01f));
        var material = Resources.Load<Material>("BeachProps/"+finish);
        Assert.IsNotNull(material.GetTexture("_BaseMap"));
        Assert.AreEqual("Universal Render Pipeline/Lit",material.shader.name);
    }

    [UnityTest] public IEnumerator CoastModelsFollowTheBeachTheme()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        BeachSearch.FollowHost(BeachTheme.Tournament);
        yield return null;
        yield return null;
        var game=Object.FindFirstObjectByType<BeachSearch>();
        void Uses(Transform prop,string model)
        {
            Assert.IsNotNull(prop,model);
            var imported=Resources.Load<GameObject>("Coast/"+model);
            Assert.AreSame(imported.GetComponentInChildren<MeshFilter>().sharedMesh,prop.GetComponent<MeshFilter>().sharedMesh,model);
        }
        Assert.IsTrue(game.spectatorArea.activeSelf);
        Uses(game.spectatorArea.transform.Find("접이식 의자"),"Chair");
        Uses(game.Volleyball.Model.Find("배구공"),"Volleyball");
        Uses(game.Volleyball.transform.Find("배구 네트"),"Net");
        Uses(game.Bucket.Model.Find("모래성 양동이"),"Bucket");
        Uses(game.player.Bins[0].transform.Find("수거함 본체"),"Bin");
        Uses(game.stall.Find("매입 가판대"),"Stall");
        Uses(game.stall.parent.Find("동쪽 인명구조대/구조 망루"),"Tower");
        BeachSearch.FollowHost(BeachTheme.Festival);
        yield return null;
        yield return null;
        game=Object.FindFirstObjectByType<BeachSearch>();
        Assert.IsFalse(game.spectatorArea.activeSelf);
        Uses(game.stall.Find("축제 깃발/축제 깃발"),"Pennant");
        yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator PhysicalBinsTakeOnlyTheMatchingLitterInHand()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game=Object.FindFirstObjectByType<BeachSearch>();
        game.Visit(game.Theme);
        var me=game.Local;
        var bag=game.inventory;
        Assert.AreEqual(3,game.player.Bins.Length);
        var bottle=game.player.Bins[(int)WasteBin.Bottle];
        me.SelectSlot(-1);
        Assert.AreEqual(BeachInventory.SlotCount-1,me.Selected);
        bag.Store(FindKind.PlasticBottle);
        yield return null;
        Assert.IsTrue(game.player.HoldingFind,"the bottle is not shown in hand");
        Assert.IsNotNull(game.player.HoldPoint.GetComponentInChildren<MeshFilter>());
        if (SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)
            Assert.IsNotNull(me.SlotIcon(new BeachSlot(FindKind.PlasticBottle)));
        game.player.transform.position=bottle.transform.position+Vector3.right;
        game.player.Follow();
        me.Deposit(game.player.Bins[(int)WasteBin.General]);
        Assert.AreEqual(0,bag.Wallet);
        Assert.AreEqual(1,bag.Count);
        me.Deposit(bottle);
        Assert.AreEqual(0,bag.Count);
        Assert.AreEqual(BeachInventory.SortedPrice,bag.Wallet);
        Assert.AreEqual(1,game.Sorted);
        Assert.AreEqual(BeachInventory.SortedPrice,game.Earned);
        Assert.IsFalse(game.player.HoldingFind);
        yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator PingReplacementAndHeatFeedbackFollowThePlayer()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game = Object.FindFirstObjectByType<BeachSearch>();
        game.Visit(game.Theme);
        game.Outbox = message => lastSent = message;
        game.Ping(Vector2.zero);
        game.Receive(lastSent,1);
        game.Ping(Vector2.right);
        game.Outbox = null;
        yield return null;
        int rings = 0;
        foreach (Transform child in game.transform)
            if (child.name=="수색 위치 핑") rings++;
        Assert.AreEqual(2,rings);
        var haze = Object.FindFirstObjectByType<UnityEngine.UI.RawImage>();
        Assert.IsNotNull(haze);
        // Drive exposure directly, independent of the spawn point and its nearby shade.
        game.Local.enabled = false;
        game.player.UpdateExposure(false,61);
        yield return null;
        yield return null;
        Assert.IsTrue(haze.enabled);
        game.player.UpdateExposure(true,10);
        float deadline = Time.time+5.2f;
        while (Time.time<deadline) yield return null;
        Assert.IsFalse(haze.enabled);
        Assert.IsNull(game.transform.Find("수색 위치 핑"));
        yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator TurtleNetCanOnlyBeCollectedOnce()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        BeachSearch.FollowHost(BeachTheme.Festival);
        yield return null;
        yield return null;
        var game = Object.FindFirstObjectByType<BeachSearch>();
        Assert.AreEqual(BeachTurtle.Stage.Entangled,game.Turtle.Current);
        game.HelpTurtle();
        Assert.AreEqual(BeachTurtle.Stage.Released,game.Turtle.Current);
        byte[] released = game.WriteSnapshot();
        game.HelpTurtle();
        game.HelpTurtle();
        Assert.AreEqual(1,game.Cleaned[FindKind.FishingNet]);
        Assert.AreEqual(FindKind.FishingNet,game.inventory.Hand);
        Assert.AreEqual(1,game.Picked);
        BeachSearch.FollowHost(BeachTheme.Festival);
        yield return null;
        yield return null;
        game = Object.FindFirstObjectByType<BeachSearch>();
        Assert.IsTrue(game.ReadSnapshot(released));
        Assert.AreEqual(BeachTurtle.Stage.Released,game.Turtle.Current);
        Assert.AreEqual(0,game.Picked);
        yield return new ExitPlayMode();
    }

    // One beach plays the host and then, after a reload, the guest who asked: the host's answers are
    // carried across by hand, as the network would.
    [UnityTest] public IEnumerator AGuestsTurtleRescueIsSettledByTheHost()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        BeachSearch.FollowHost(BeachTheme.Festival);
        yield return null;
        yield return null;
        var host = Object.FindFirstObjectByType<BeachSearch>();
        var answers = new System.Collections.Generic.List<byte[]>();
        host.Outbox = answers.Add;
        byte[] untangle = {(byte)BeachSearch.Op.TurtleRequest,(byte)BeachTurtle.Stage.Entangled};
        byte[] collect = {(byte)BeachSearch.Op.TurtleRequest,(byte)BeachTurtle.Stage.Released};
        Assert.IsFalse(host.Receive(untangle,1),"a request is for the host alone");
        Assert.AreEqual(BeachTurtle.Stage.Released,host.Turtle.Current);
        // A second press that left before the answer arrived still names the old stage.
        host.Receive(untangle,1);
        Assert.AreEqual(BeachTurtle.Stage.Released,host.Turtle.Current);
        host.Receive(collect,1);
        host.Receive(collect,2);
        Assert.AreEqual(BeachTurtle.Stage.Collected,host.Turtle.Current);
        Assert.AreEqual(2,answers.Count,"each stage is announced once");
        Assert.AreEqual(1,host.Cleaned[FindKind.FishingNet]);
        Assert.AreEqual(1,host.Picked);
        Assert.AreEqual(0,host.inventory.Count,"the net belongs to the guest who collected it");
        host.Outbox = null;

        BeachSearch.FollowHost(BeachTheme.Festival);
        yield return null;
        yield return null;
        var guest = Object.FindFirstObjectByType<BeachSearch>();
        Assert.IsNotNull(guest,"the beach did not reload");
        Assert.IsNotNull(guest.Turtle,"the reloaded beach has no turtle");
        guest.Guest = true;
        guest.MyId = 1;
        // Kept in a static: a local captured before play mode began does not survive the reload.
        lastSent = null;
        guest.Outbox = message => lastSent = message;
        guest.HelpTurtle();
        CollectionAssert.AreEqual(untangle,lastSent);
        Assert.AreEqual(BeachTurtle.Stage.Entangled,guest.Turtle.Current,"a guest waits for the host's word");
        guest.Receive(answers[0],0);
        Assert.AreEqual(BeachTurtle.Stage.Released,guest.Turtle.Current);
        guest.Receive(answers[1],0);
        Assert.AreEqual(BeachTurtle.Stage.Collected,guest.Turtle.Current);
        Assert.AreEqual(1,guest.Cleaned[FindKind.FishingNet]);
        Assert.AreEqual(FindKind.FishingNet,guest.inventory.Hand);
        guest.Outbox = null;
        yield return new ExitPlayMode();
    }

    [UnityTest] public IEnumerator AHandfulCarriesACoinThatCanThenBePickedUp()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;

        var game = Object.FindFirstObjectByType<BeachSearch>();
        Assert.IsNotNull(game,"the scene has no BeachSearch");
        Assert.IsNotNull(game.Local,"the beach did not hand its player to a BeachDigger");
        var me = game.Local;
        game.Visit(game.Theme);
        Assert.IsTrue(game.Started);
        Assert.GreaterOrEqual(game.TotalFinds,29,"the extra finds and the marine litter were not buried");

        var coin = System.Array.Find(game.finds,item=>item.kind==FindKind.Coin);
        Vector2 hole = coin.Position;
        float level = game.sand.Height(hole);
        me.HandScoop(hole);
        Assert.IsTrue(me.Loaded,"scooping lifted no sand");
        Assert.Less(game.sand.Height(hole),level,"the scoop left no hole");
        Assert.IsFalse(coin.gameObject.activeSelf,"the coin should travel in the handful");

        Vector2 heap = game.sand.ClampToBeach(hole+new Vector2(.9f,0),.5f);
        me.HandScoop(heap);
        Assert.IsFalse(me.Loaded,"the handful was not set down");
        Assert.IsTrue(coin.gameObject.activeSelf,"the coin should land with the sand");
        Assert.IsTrue(coin.Exposed(game.sand));

        me.Take(coin);
        Assert.IsFalse(coin.gameObject.activeSelf);
        Assert.AreEqual(1,game.Picked);
        Assert.AreEqual(1,game.inventory.Count);
        Assert.IsFalse(game.Won);
        yield return null;

        yield return new ExitPlayMode();
    }
}
