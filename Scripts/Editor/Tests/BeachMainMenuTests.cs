using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class BeachMainMenuTests
{
    static Button Action(BeachHUD hud,string caption) => hud.Canvas.Canvas
        .GetComponentsInChildren<Button>()
        .Single(b=>b.enabled && b.GetComponentInChildren<Text>().text==caption);

    [UnityTest]
    public IEnumerator MainMenuRoutesToHelpJoinAndSoloWithoutStartingEarly()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var game=Object.FindFirstObjectByType<BeachSearch>();
        var hud=Object.FindFirstObjectByType<BeachHUD>();
        Assert.IsFalse(game.Started);
        var figure=game.player.view.WorldToViewportPoint(game.player.transform.position+Vector3.up*.85f);
        Assert.That(figure.x,Is.InRange(.64f,.76f));
        Assert.That(figure.y,Is.EqualTo(.5f).Within(.03f));
        Action(hud,"플레이 방법").onClick.Invoke();
        yield return null;
        yield return null;
        Action(hud,"돌아가기").onClick.Invoke();
        yield return null;
        yield return null;
        Action(hud,"방 참가").onClick.Invoke();
        yield return null;
        yield return null;
        Assert.IsTrue(BeachNet.Instance.Typing);
        Action(hud,"취소").onClick.Invoke();
        yield return null;
        yield return null;
        Assert.IsFalse(BeachNet.Instance.Typing);
        Action(hud,"혼자 찾기").onClick.Invoke();
        yield return null;
        yield return null;
        Assert.IsFalse(game.Started);
        Action(hud,">").onClick.Invoke();
        yield return null;
        yield return null;
        Assert.AreEqual(1,BeachNet.SelectedLook);

        Action(hud,"준비 완료").onClick.Invoke();
        yield return null;
        yield return null;
        Action(hud,"해변 선택 →").onClick.Invoke();
        yield return null;
        yield return null;
        Action(hud,BeachSearch.ThemeName(BeachTheme.Neighborhood)).onClick.Invoke();
        yield return null;
        yield return null;
        Assert.IsTrue(game.Started);
        Assert.IsFalse(game.player.Presenting);
        Assert.IsFalse(BeachNet.Instance.Online);
        yield return new ExitPlayMode();
    }
    [UnityTest]
    public IEnumerator HostReadinessIsClearedWhenAppearanceChanges()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        yield return null;
        var net=BeachNet.Ensure();
        net.Host();
        yield return null;
        Assert.IsTrue(net.Hosting);
        Assert.IsFalse(net.AllReady);
        net.SetLobbyChoice(BeachNet.SelectedLook,true);
        Assert.IsTrue(net.AllReady);
        BeachNet.SelectLook(BeachBody.LookCount-1);
        Assert.IsFalse(net.LocalReady);
        Assert.IsFalse(net.AllReady);
        Assert.AreEqual(BeachBody.LookCount-1,net.Members.Single().Look);
        net.Leave();
        Assert.IsEmpty(net.Members);
        yield return new ExitPlayMode();
    }

}
