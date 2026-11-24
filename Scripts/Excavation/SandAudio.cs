using UnityEngine;

// Sand sounds made from filtered noise, so no audio assets are needed.
public static class SandAudio
{
    const int Rate = 32000;

    // `smooth` near 1 gives a dull thud, lower values a brighter hiss. `attack` is the rise as a share of the clip.
    static AudioClip Noise(string name,float seconds,float smooth,float attack,float loudness,int seed)
    {
        var random = new System.Random(seed);
        var samples = new float[Mathf.RoundToInt(seconds*Rate)];
        float low = 0;
        for (int i=0; i<samples.Length; i++)
        {
            low = Mathf.Lerp((float)random.NextDouble()*2f-1f,low,smooth);
            float t = (float)i/samples.Length;
            float envelope = t<attack ? t/attack : Mathf.Pow(1f-(t-attack)/(1f-attack),2f);
            samples[i] = low*envelope*loudness;
        }
        var clip = AudioClip.Create(name,samples.Length,1,Rate,false);
        clip.SetData(samples,0);
        return clip;
    }

    public static AudioClip Footstep() => Noise("모래 발소리",.13f,.86f,.08f,1.5f,7);
    public static AudioClip Pour() => Noise("모래 쏟는 소리",.48f,.55f,.18f,.50f,11);
    public static AudioClip Trickle() => Noise("모래 흘러내리는 소리",.30f,.40f,.30f,.26f,19);
}
