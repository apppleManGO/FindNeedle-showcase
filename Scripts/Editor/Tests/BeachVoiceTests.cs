using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

// Talking to each other. Steam is not here, so decoded samples are handed in directly; what is checked is
// the buffering and where each voice plays from.
public class BeachVoiceTests
{
    static float[] Tone(int count,float value)
    {
        var samples = new float[count];
        for (int i=0; i<count; i++) samples[i] = value;
        return samples;
    }

    [Test] public void AVoiceWaitsForALittleBufferThenPlays()
    {
        var stream = new VoiceStream(1000,startSeconds:.1f);
        var output = new float[50];
        stream.Write(Tone(60,.5f));
        stream.Read(output);
        Assert.AreEqual(0f,output[0],"played before enough had arrived");
        Assert.AreEqual(60,stream.Buffered);

        stream.Write(Tone(60,.5f));
        stream.Read(output);
        Assert.AreEqual(.5f,output[0]);
        Assert.AreEqual(.5f,output[49]);
        Assert.IsTrue(stream.Playing);
    }

    [Test] public void AGapGoesQuietAndBuffersAgain()
    {
        var stream = new VoiceStream(1000,startSeconds:.1f);
        stream.Write(Tone(100,.5f));
        var output = new float[150];
        stream.Read(output);
        Assert.AreEqual(.5f,output[99]);
        Assert.AreEqual(0f,output[100],"no silence when the voice ran out");
        Assert.IsFalse(stream.Playing);

        // A short packet after the gap waits for the buffer to fill again.
        stream.Write(Tone(30,.5f));
        var next = new float[10];
        stream.Read(next);
        Assert.AreEqual(0f,next[0]);
    }

    [Test] public void AVoiceNeverLagsFarBehind()
    {
        var stream = new VoiceStream(1000,startSeconds:.1f,mostSeconds:.5f,trimSeconds:.2f);
        stream.Write(Tone(800,.5f));
        Assert.LessOrEqual(stream.Buffered,200,"a pile-up was kept instead of trimmed");
    }

    [Test] public void SixteenBitSamplesAreRead()
    {
        var stream = new VoiceStream(1000,startSeconds:.001f);
        // 16384 and -16384, little-endian.
        stream.WritePcm16(new byte[]{0x00,0x40,0x00,0xC0},4);
        var output = new float[2];
        stream.Read(output);
        Assert.AreEqual(.5f,output[0],1e-4f);
        Assert.AreEqual(-.5f,output[1],1e-4f);
    }

    static Transform standing;
    static Transform Standing(int id) => id==1 ? standing : null;

    [UnityTest] public IEnumerator EachVoicePlaysFromItsSpeakersHead()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        yield return null;
        var voice = new GameObject("voice test").AddComponent<BeachVoice>();
        standing = new GameObject("player 1").transform;
        standing.position = new Vector3(3,0,2);
        voice.Figure = Standing;

        voice.HeardSamples(1,Tone(4800,.2f),24000);
        voice.HeardSamples(2,Tone(4800,.2f),24000);
        yield return null;
        var near = voice.SourceOf(1);
        var lobby = voice.SourceOf(2);
        Assert.IsNotNull(near);
        Assert.IsTrue(near.isPlaying);
        Assert.AreEqual(1f,near.spatialBlend,"a voice on the beach should come from a place");
        Assert.Less(Vector3.Distance(near.transform.position,standing.position+Vector3.up*1.4f),.01f,"the voice is not at their head");
        Assert.AreEqual(AudioRolloffMode.Linear,near.rolloffMode);
        Assert.AreEqual(BeachVoice.Near,near.minDistance);
        Assert.AreEqual(BeachVoice.Far,near.maxDistance);
        Assert.AreEqual(0f,lobby.spatialBlend,"someone with no figure here should simply be heard");
        Assert.IsTrue(voice.Talking(1));
        Assert.IsFalse(voice.Talking(3));

        // They walk away; the voice follows.
        standing.position = new Vector3(-4,0,-1);
        yield return null;
        Assert.Less(Vector3.Distance(near.transform.position,standing.position+Vector3.up*1.4f),.01f);

        voice.Forget(1);
        yield return null;
        Assert.IsNull(voice.SourceOf(1));
        Assert.IsFalse(voice.Talking(1));
        voice.Clear();
        Assert.IsNull(voice.SourceOf(2));
        Assert.IsFalse(voice.Muted);
        voice.ToggleMute();
        Assert.IsTrue(voice.Muted);
        yield return new ExitPlayMode();
    }
}
