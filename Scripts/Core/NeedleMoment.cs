using System.Collections.Generic;
using UnityEngine;

// The few seconds after the needle comes up, before the result panel. The finder lifts the needle into view,
// then the camera draws back as they hold it overhead, and the search time is stamped on the screen.
// In co-op everyone's camera turns to whoever found it, and the rest of the team cheers with them.
// Only the picture slows; the beach keeps its own time, so the players' machines cannot drift apart.
// Runs after the figures have posed their arms, so the needle follows the raised hand.
[DefaultExecutionOrder(200)]
public sealed class NeedleMoment : MonoBehaviour
{
    // When each part begins, in seconds from the pick-up.
    public const float Raise = .3f, Reveal = 1.3f, Stamp = 2.5f, Record = 2.8f, End = 3.5f;

    BeachSearch game;
    AudioSource speaker;
    AudioClip ping, sparkle, thud;
    float start = -1;
    int finder;
    BeachGhost finderFigure;
    GameObject needle;
    Transform star;
    float baseFov;
    Quaternion fromLook;
    Vector3 closeEye;
    Quaternion closeLook;
    bool revealed, stamped, recorded, ended;

    public bool Playing => start>=0 && !ended;
    public float Elapsed => start<0 ? 0 : Time.time-start;
    public int Finder => finder;
    public bool FoundHere => finder==game.MyId;
    // How much of the screen above the result panel is free, written by the HUD so the last shot lines up with it.
    public float PortraitFree { get; set; } = .6f;
    // How dark the screen edges are, 0 to 1.
    public float Vignette
    {
        get
        {
            if (!Playing) return 0;
            float t = Elapsed;
            return t<Raise ? t/Raise : t<Reveal ? 1 : Mathf.Clamp01(1-(t-Reveal)/.5f);
        }
    }

    public void Initialize(BeachSearch beach)
    {
        game = beach;
        speaker = gameObject.AddComponent<AudioSource>();
        speaker.playOnAwake = false;
        ping = Ping();
        sparkle = Sparkle();
        thud = Thud();
    }

    public void Begin(int who)
    {
        if (start>=0) return;
        finder = who;
        start = Time.time;
        game.Local.Freeze();
        var view = game.player.view;
        baseFov = view.fieldOfView;
        fromLook = view.transform.rotation;
        speaker.PlayOneShot(ping,.9f);
        if (!FoundHere && game.Team!=null && game.Team().TryGetValue(who,out var figure) && figure) finderFigure = figure;
        if (FoundHere)
        {
            // In the finder's own view: the needle comes up from the hand to the middle of the screen.
            needle = NeedleCopy(view.transform,.22f);
            if (needle) needle.transform.localPosition = new Vector3(.13f,-.24f,.42f);
        }
    }

    // `boost` draws it larger than life, so a needle held overhead still reads from a few metres away.
    GameObject NeedleCopy(Transform parent,float longest,float boost=1)
    {
        var slot = new BeachSlot(FindKind.Needle);
        var source = game.Local.ModelFor(slot);
        if (!source || !game.Local.Models) return null;
        var copy = game.Local.Models.Carried(slot,source,parent,longest);
        if (!copy) return null;
        copy.name = "찾은 바늘";
        copy.transform.localScale *= boost;
        star = Star(copy.transform).transform;
        return copy;
    }

    // An eight-pointed glint, like the one on a needle spread over flat sand.
    GameObject Star(Transform parent)
    {
        var flash = new GameObject("바늘 반짝임");
        flash.transform.SetParent(parent,false);
        var line = flash.AddComponent<LineRenderer>();
        line.sharedMaterial = game.RingMaterial;
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 8;
        line.widthMultiplier = .008f;
        line.startColor = line.endColor = new Color(1f,.97f,.8f);
        for (int i=0; i<8; i++)
        {
            float angle = i*Mathf.PI/4;
            float length = i%2==0 ? .09f : .016f;
            line.SetPosition(i,new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0)*length);
        }
        return flash;
    }

    void LateUpdate()
    {
        if (!Playing) return;
        float t = Elapsed;
        var view = game.player.view;
        if (!revealed && t>=Reveal) RevealFinder();
        if (!stamped && t>=Stamp) { stamped = true; speaker.PlayOneShot(thud,.9f); }
        if (!recorded && t>=Record && game.NewRecord) { recorded = true; speaker.PlayOneShot(thud,.7f); speaker.PlayOneShot(sparkle,.5f); }
        if (t>=End) { Finish(); return; }

        if (!revealed)
        {
            if (FoundHere && needle)
            {
                float k = Smooth(Mathf.InverseLerp(Raise,Reveal-.15f,t));
                needle.transform.localPosition = Vector3.Lerp(new Vector3(.13f,-.24f,.42f),new Vector3(0,-.015f,.34f),k);
                needle.transform.localRotation = Quaternion.Euler(-20f+10f*k,-70f+40f*k+25f*t,35f-10f*k);
                view.fieldOfView = Mathf.Lerp(baseFov,baseFov-14f,k);
                // The eyes follow the needle up off the sand, so it is held against the sea and sky.
                var level = Quaternion.Euler(-6f,fromLook.eulerAngles.y,0);
                view.transform.rotation = Quaternion.Slerp(fromLook,level,k);
                if (t>=Raise && !sparkled) { sparkled = true; speaker.PlayOneShot(sparkle,.6f); }
            }
            else if (finderFigure)
            {
                // Everyone else turns to look at the finder.
                Vector3 chest = finderFigure.transform.position+Vector3.up*1.1f;
                var toward = Quaternion.LookRotation(chest-view.transform.position);
                view.transform.rotation = Quaternion.Slerp(fromLook,toward,Smooth(Mathf.InverseLerp(0,Reveal-.2f,t)));
            }
        }
        else if (t<Stamp+.2f)
        {
            // Draw back from close up to where the result shot will stand.
            float k = Smooth(Mathf.InverseLerp(Reveal,Stamp+.2f,t));
            if (finderFigure) FrameFigure(view.transform,Mathf.Lerp(1.3f,2.6f,k));
            else
            {
                game.player.FramePortrait(PortraitFree);
                Vector3 eye = view.transform.position;
                Quaternion look = view.transform.rotation;
                view.transform.SetPositionAndRotation(Vector3.Lerp(closeEye,eye,k),Quaternion.Slerp(closeLook,look,k));
            }
        }
        else if (finderFigure) FrameFigure(view.transform,2.6f);
        else game.player.FramePortrait(PortraitFree);

        HoldNeedle(t);
    }

    bool sparkled;

    void RevealFinder()
    {
        revealed = true;
        var view = game.player.view;
        view.fieldOfView = baseFov;
        if (needle) { Destroy(needle); needle = null; }
        // Out of first person: the finder's figure, or this player's own, stands with arms up.
        game.Local.ShowResult();
        BeachBody hero;
        Vector3 feet;
        if (finderFigure)
        {
            finderFigure.Cheering = true;
            hero = finderFigure.Body;
            feet = finderFigure.transform.position;
        }
        else
        {
            // Someone else found it but their figure is not here: this player cheers empty-handed.
            hero = FoundHere ? game.player.Body : null;
            feet = game.player.transform.position;
            // Start close in front of the figure, looking up at the raised needle.
            var me = game.player.transform;
            closeEye = feet-me.forward*1.05f+Vector3.up*1.05f;
            closeLook = Quaternion.LookRotation(feet+Vector3.up*1.35f-closeEye);
            view.transform.SetPositionAndRotation(closeEye,closeLook);
        }
        if (game.Team!=null)
            foreach (var mate in game.Team().Values)
                if (mate) mate.Cheering = true;
        // Seen from a few metres off, a teammate's needle is drawn bigger than the finder's own close shot.
        if (hero) needle = NeedleCopy(hero.transform.parent,.3f,finderFigure ? 2.4f : 1.3f);
        game.sand.Kick(new Vector2(feet.x,feet.z),26,.45f,1.6f);
        game.PlayFanfare();
    }

    // In front of the finder's figure, `distance` away, looking at their chest.
    void FrameFigure(Transform view,float distance)
    {
        var figure = finderFigure.transform;
        Vector3 chest = figure.position+Vector3.up*1.05f;
        Vector3 eye = chest+figure.forward*distance+Vector3.up*(.1f+.08f*distance);
        view.SetPositionAndRotation(eye,Quaternion.LookRotation(chest+Vector3.up*.15f-eye));
    }

    // Keeps the needle in the raised right hand, and the glint turned to the camera.
    void HoldNeedle(float t)
    {
        if (star)
        {
            star.rotation = game.player.view.transform.rotation*Quaternion.Euler(0,0,t*90f);
            float pulse = .75f+.35f*Mathf.Sin(t*9f);
            star.localScale = Vector3.one*pulse/Mathf.Max(.001f,star.parent.lossyScale.x)*(revealed ? .7f : .25f);
        }
        if (!revealed || !needle) return;
        var hero = finderFigure ? finderFigure.Body : game.player.Body;
        var hand = hero ? hero.RightHand : null;
        if (!hand) return;
        var up = hero.transform.parent ? hero.transform.parent.rotation : Quaternion.identity;
        needle.transform.SetPositionAndRotation(hand.position+Vector3.up*.06f,up*Quaternion.Euler(0,0,70f));
    }

    void Finish()
    {
        ended = true;
        game.player.view.fieldOfView = baseFov;
        // The result panel takes over; the finder keeps holding the needle up behind it.
        if (finderFigure) game.Local.ShowResult();
    }

    static float Smooth(float k) => k*k*(3-2*k);

    // ---- Sounds, made here like the game's other tones ----

    const int Rate = 32000;

    static AudioClip Make(string name,float seconds,System.Func<float,float> wave)
    {
        var samples = new float[Mathf.RoundToInt(seconds*Rate)];
        for (int i=0; i<samples.Length; i++) samples[i] = wave((float)i/Rate);
        var clip = AudioClip.Create(name,samples.Length,1,Rate,false);
        clip.SetData(samples,0);
        return clip;
    }

    // A small bright metal ring: three partials dying away.
    static AudioClip Ping() => Make("바늘 팅",1.1f,t =>
        Mathf.Exp(-4.5f*t)*(.16f*Mathf.Sin(2*Mathf.PI*2637*t)+.09f*Mathf.Sin(2*Mathf.PI*3951*t)+.05f*Mathf.Sin(2*Mathf.PI*5274*t))
        *Mathf.Clamp01(t*400));

    // Four quick rising notes.
    static AudioClip Sparkle() => Make("반짝",.42f,t =>
    {
        float[] notes = {1568,2093,2637,3136};
        int n = Mathf.Min(3,(int)(t/.07f));
        float local = t-n*.07f;
        return .1f*Mathf.Sin(2*Mathf.PI*notes[n]*t)*Mathf.Exp(-9f*local)*Mathf.Clamp01(local*300);
    });

    // The time stamped on the screen: a low knock with a little grit.
    static AudioClip Thud()
    {
        var noise = new System.Random(7);
        return Make("도장",.3f,t =>
            Mathf.Exp(-16f*t)*(.32f*Mathf.Sin(2*Mathf.PI*(95-60*t)*t)+.08f*((float)noise.NextDouble()*2-1)*Mathf.Exp(-30f*t)));
    }

    void OnDestroy()
    {
        if (ping) Destroy(ping);
        if (sparkle) Destroy(sparkle);
        if (thud) Destroy(thud);
    }
}
