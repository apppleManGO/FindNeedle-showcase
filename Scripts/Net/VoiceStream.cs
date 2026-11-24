using System;

// One player's voice waiting to be played: decoded samples go in as packets arrive, and the audio thread
// takes them out. Playback starts only once a little is buffered, so packets arriving unevenly do not
// stutter. When the speaker stops, it goes quiet and waits to buffer again. If too much piles up, the oldest
// is dropped, so a voice never lags far behind its speaker.
public sealed class VoiceStream
{
    readonly float[] ring;
    readonly int startAt, keepAtMost, trimTo;
    int head, count;
    bool waiting = true;
    readonly object guard = new();

    public int SampleRate { get; }
    public int Buffered { get { lock (guard) return count; } }
    public bool Playing { get { lock (guard) return !waiting; } }

    public VoiceStream(int sampleRate,float startSeconds = .12f,float mostSeconds = .5f,float trimSeconds = .2f)
    {
        SampleRate = sampleRate;
        startAt = Math.Max(1,(int)(sampleRate*startSeconds));
        keepAtMost = (int)(sampleRate*mostSeconds);
        trimTo = (int)(sampleRate*trimSeconds);
        ring = new float[Math.Max(keepAtMost*2,sampleRate)];
    }

    // 16-bit little-endian mono, as Steam decodes voice.
    public void WritePcm16(byte[] pcm,int bytes)
    {
        lock (guard)
        {
            for (int i=0; i+1<bytes; i+=2)
            {
                short sample = (short)(pcm[i] | pcm[i+1]<<8);
                Push(sample/32768f);
            }
            if (count>keepAtMost) Drop(count-trimTo);
        }
    }

    public void Write(float[] samples)
    {
        lock (guard)
        {
            foreach (float sample in samples) Push(sample);
            if (count>keepAtMost) Drop(count-trimTo);
        }
    }

    void Push(float sample)
    {
        if (count==ring.Length) Drop(1);
        ring[(head+count)%ring.Length] = sample;
        count++;
    }

    void Drop(int samples)
    {
        samples = Math.Min(samples,count);
        head = (head+samples)%ring.Length;
        count -= samples;
    }

    // Fills `output` (mono). Silence while buffering.
    public void Read(float[] output)
    {
        lock (guard)
        {
            if (waiting && count>=startAt) waiting = false;
            for (int i=0; i<output.Length; i++)
            {
                if (waiting || count==0)
                {
                    output[i] = 0;
                    if (count==0) waiting = true;
                    continue;
                }
                output[i] = ring[head];
                head = (head+1)%ring.Length;
                count--;
            }
        }
    }

    public void Clear()
    {
        lock (guard) { head = count = 0; waiting = true; }
    }
}
