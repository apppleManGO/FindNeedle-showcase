#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Development builds only. Started with `-beachAuto host` or `-beachAuto join`, it plays a short scripted
// session without a person and writes one BEACH-REPORT line to the log, so two copies running on one
// machine can be compared: both must report the same sand, the same finds and one other player.
public sealed class BeachAutopilot : MonoBehaviour
{
    string role;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        var args = System.Environment.GetCommandLineArgs();
        int at = System.Array.IndexOf(args,"-beachAuto");
        if (at<0 || at+1>=args.Length) return;
        var pilot = new GameObject("자동 시연").AddComponent<BeachAutopilot>();
        pilot.role = args[at+1];
        DontDestroyOnLoad(pilot.gameObject);
    }

    static BeachSearch Game => FindFirstObjectByType<BeachSearch>();

    IEnumerator Start()
    {
        Application.runInBackground = true;
        StartCoroutine(GiveUp());
        bool host = role=="host";
        if (host)
        {
            yield return new WaitForSecondsRealtime(1f);
            BeachNet.Ensure().Host(false);
            Game.Visit(BeachTheme.Neighborhood);
        }
        else
        {
            yield return new WaitForSecondsRealtime(4f);
            BeachNet.Ensure().Join("127.0.0.1");
        }
        // Both wait until each can see the other standing on the same beach.
        while (!Game || !Game.Started || BeachNet.Instance.Seen<1) yield return null;
        yield return new WaitForSecondsRealtime(1f);

        var game = Game;
        var me = game.Local;
        float side = host ? -1 : 1;
        if (!host)
        {
            // The guest lifts the coin out, sets it down and asks the host for it.
            var coin = System.Array.Find(game.finds,item=>item.kind==FindKind.Coin && item.gameObject.activeSelf);
            Vector2 hole = coin.Position;
            me.HandScoop(hole);
            yield return new WaitForSecondsRealtime(.4f);
            me.HandScoop(game.sand.ClampToBeach(hole+new Vector2(.9f,0),.5f));
            yield return new WaitForSecondsRealtime(.4f);
            me.Take(coin);
            yield return new WaitForSecondsRealtime(.4f);
        }
        for (int i=0; i<4; i++)
        {
            Vector2 spot = new Vector2(side*(2f+.5f*i),3.2f);
            me.HandScoop(spot);
            yield return new WaitForSecondsRealtime(.3f);
            me.HandScoop(spot+new Vector2(0,.8f));
            yield return new WaitForSecondsRealtime(.3f);
        }
        // A pit deep enough that its sides slide for a while.
        Vector2 pit = new Vector2(side*3.5f,-3.5f);
        var scrap = new List<BeachFind>();
        for (int i=0; i<4; i++) { scrap.Clear(); game.ScoopSand(pit,.45f,.14f,scrap,true,Vector2.up); }
        game.FinishStroke(pit,.45f,true);

        // The clock is shared, so both stop at the same moment of the visit.
        while (game.Elapsed<30f) yield return null;
        Report(game);
        Application.Quit();
    }

    IEnumerator GiveUp()
    {
        yield return new WaitForSecondsRealtime(120f);
        Debug.Log("BEACH-REPORT role="+role+" FAILED: no session after two minutes · "+(BeachNet.Instance ? BeachNet.Instance.Status : "no net"));
        Application.Quit(1);
    }

    void Report(BeachSearch game)
    {
        var heights = game.sand.Heights;
        double total = 0;
        uint exact = 2166136261, coarse = 2166136261;
        foreach (float h in heights)
        {
            total += h;
            exact = (exact^(uint)System.BitConverter.SingleToInt32Bits(h))*16777619;
            coarse = (coarse^(uint)Mathf.RoundToInt(h*200f))*16777619;
        }
        var shown = new System.Text.StringBuilder();
        foreach (var item in game.finds) shown.Append(item.gameObject.activeSelf ? '1' : '0');
        Debug.Log($"BEACH-REPORT role={role} seen={BeachNet.Instance.Seen} picked={game.Picked} bag={game.inventory.Count} "+
            $"finds={shown} sandTotal={total:0.0000} sandCoarse={coarse:X8} sandExact={exact:X8}");
    }
}
#endif
