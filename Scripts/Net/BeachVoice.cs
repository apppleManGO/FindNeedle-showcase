#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX
#define FINDNEEDLE_STEAM
#endif
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
#if FINDNEEDLE_STEAM
using Steamworks;
#endif

// Talking to each other. Steam records the microphone and compresses it; the bytes go to the others through
// the session like any other message, so voice works whether the room was joined by Steam invite or by
// address, as long as Steam is running. Each teammate's voice plays from their figure's head and fades
// with distance, so who you hear depends on where you stand. In the lobby, before anyone stands on the
// beach, voices play flat. V turns this player's microphone off and on.
public sealed class BeachVoice : MonoBehaviour
{
    // Full volume within Near metres, silent beyond Far.
    public const float Near = 1.5f, Far = 9f;
    // A voice packet larger than this is dropped rather than risk the unreliable channel's size limit.
    const int MostBytes = 1100;

    // Set by the session: sends one compressed packet to the others.
    public System.Action<byte[]> Outbox;
    // Set by the session: a teammate's figure on the beach, or null when they have none here.
    public System.Func<int,Transform> Figure;

    public bool Muted { get; private set; }
    // Voice needs the Steam client on this machine, to record and to decode.
    public static bool Available => BeachNet.SteamReady || Simulated;
    // Tests and screenshots, which have no Steam: show the microphone as present, without recording anything.
    public static bool Simulated;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => Simulated = false;
    static bool Steam => BeachNet.SteamReady;
    public bool LocalTalking => Time.unscaledTime-lastSent < .3f;

    readonly Dictionary<int,Speaker> speakers = new();
    float lastSent = -9;
    bool recording;

    sealed class Speaker
    {
        public AudioSource source;
        public VoiceStream stream;
        public float heard = -9;
    }

    public bool Talking(int id) => speakers.TryGetValue(id,out var speaker) && Time.unscaledTime-speaker.heard < .3f;

    public void ToggleMute() => Muted = !Muted;

    // ---- Sending ----

#if FINDNEEDLE_STEAM
    readonly byte[] captured = new byte[8192];
    byte[] decoded = new byte[48000];
#endif

    void Update()
    {
        var keys = Keyboard.current;
        bool live = Outbox!=null && Available;
        if (live && keys!=null && keys.vKey.wasPressedThisFrame) ToggleMute();
        SetRecording(live && !Muted);
#if FINDNEEDLE_STEAM
        if (!recording || !Steam) return;
        if (SteamUser.GetAvailableVoice(out uint waiting)!=EVoiceResult.k_EVoiceResultOK || waiting==0) return;
        if (SteamUser.GetVoice(true,captured,(uint)captured.Length,out uint written)!=EVoiceResult.k_EVoiceResultOK || written==0) return;
        if (written>MostBytes) return;
        var packet = new byte[written];
        System.Buffer.BlockCopy(captured,0,packet,0,(int)written);
        lastSent = Time.unscaledTime;
        Outbox(packet);
#endif
    }

    void SetRecording(bool on)
    {
        if (on==recording) return;
        recording = on;
#if FINDNEEDLE_STEAM
        if (!Steam) return;
        if (on) SteamUser.StartVoiceRecording(); else SteamUser.StopVoiceRecording();
#endif
    }

    // ---- Hearing ----

    // A compressed packet from player `id`.
    public void Heard(int id,byte[] packet)
    {
#if FINDNEEDLE_STEAM
        if (!Steam || packet.Length==0) return;
        uint rate = SteamUser.GetVoiceOptimalSampleRate();
        var result = SteamUser.DecompressVoice(packet,(uint)packet.Length,decoded,(uint)decoded.Length,out uint written,rate);
        if (result==EVoiceResult.k_EVoiceResultBufferTooSmall)
        {
            decoded = new byte[written*2];
            result = SteamUser.DecompressVoice(packet,(uint)packet.Length,decoded,(uint)decoded.Length,out written,rate);
        }
        if (result!=EVoiceResult.k_EVoiceResultOK || written==0) return;
        var speaker = SpeakerFor(id,(int)rate);
        speaker.stream.WritePcm16(decoded,(int)written);
        speaker.heard = Time.unscaledTime;
#endif
    }

    // Decoded samples from player `id`, at `rate`. The way in for tests, which have no Steam.
    public void HeardSamples(int id,float[] samples,int rate)
    {
        var speaker = SpeakerFor(id,rate);
        speaker.stream.Write(samples);
        speaker.heard = Time.unscaledTime;
    }

    Speaker SpeakerFor(int id,int rate)
    {
        if (speakers.TryGetValue(id,out var speaker) && speaker.source && speaker.stream.SampleRate==rate) return speaker;
        Forget(id);
        var holder = new GameObject("목소리 "+id);
        holder.transform.SetParent(transform,false);
        var source = holder.AddComponent<AudioSource>();
        var stream = new VoiceStream(rate);
        source.clip = AudioClip.Create("목소리 "+id,rate,1,rate,true,stream.Read);
        source.loop = true;
        source.playOnAwake = false;
        source.dopplerLevel = 0;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = Near;
        source.maxDistance = Far;
        source.spread = 40;
        source.Play();
        speaker = new Speaker{source = source,stream = stream};
        speakers[id] = speaker;
        return speaker;
    }

    public AudioSource SourceOf(int id) => speakers.TryGetValue(id,out var speaker) ? speaker.source : null;

    void LateUpdate()
    {
        foreach (var pair in speakers)
        {
            var source = pair.Value.source;
            if (!source) continue;
            var figure = Figure?.Invoke(pair.Key);
            // On the beach the voice comes from the head and fades with distance; elsewhere it is simply heard.
            source.spatialBlend = figure ? 1 : 0;
            if (figure) source.transform.position = figure.position+Vector3.up*1.4f;
        }
    }

    public void Forget(int id)
    {
        if (speakers.Remove(id,out var speaker)) Drop(speaker);
    }

    static void Drop(Speaker speaker)
    {
        if (!speaker.source) return;
        if (speaker.source.clip) Destroy(speaker.source.clip);
        Destroy(speaker.source.gameObject);
    }

    // The session is over.
    public void Clear()
    {
        foreach (var speaker in speakers.Values) Drop(speaker);
        speakers.Clear();
        Outbox = null;
        Figure = null;
        SetRecording(false);
    }

    void OnDestroy()
    {
        foreach (var speaker in speakers.Values) Drop(speaker);
        SetRecording(false);
    }
}
