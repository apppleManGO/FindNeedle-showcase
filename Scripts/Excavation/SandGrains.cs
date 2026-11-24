using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Loose grains: kicked up by a stroke, falling from hands or blade, rolling down a slide.
sealed class SandGrains
{
    readonly ParticleSystem system;
    readonly List<Stream> streams = new();

    struct Stream { public Vector3 from, to; public float left, rate, owed, spread; }

    public SandGrains(Transform parent,Material sand)
    {
        var holder = new GameObject("모래 알갱이");
        holder.transform.SetParent(parent,false);
        system = holder.AddComponent<ParticleSystem>();
        system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 1f;
        main.maxParticles = 700;
        main.startSize3D = false;
        var emission = system.emission;
        emission.enabled = false;
        var shape = system.shape;
        shape.enabled = false;
        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        renderer.sharedMaterial = sand;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.alignment = ParticleSystemRenderSpace.World;
    }

    void Grain(Vector3 position,Vector3 velocity,float life)
    {
        var grain = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = velocity,
            startLifetime = life,
            startSize = Random.Range(.003f,.008f),
            rotation3D = new Vector3(Random.value,Random.value,Random.value)*360f,
            startColor = Color.white
        };
        system.Emit(grain,1);
    }

    // Grains thrown outwards from a point on the surface.
    public void Kick(Vector3 surface,int count,float radius,float speed)
    {
        for (int i=0; i<count; i++)
        {
            Vector2 ring = Random.insideUnitCircle;
            Vector3 start = surface+new Vector3(ring.x,0,ring.y)*radius+Vector3.up*.01f;
            float up = speed*Random.Range(.5f,1.1f);
            Vector3 velocity = new Vector3(ring.x,0,ring.y)*speed*.7f+Vector3.up*up;
            Grain(start,velocity,2f*up/9.81f+.05f);
        }
    }

    // A thin fall of sand from `from` to the ground at `to`, lasting `seconds`.
    public void Fall(Vector3 from,Vector3 to,float seconds,float rate,float spread)
    {
        streams.Add(new Stream { from=from, to=to, left=seconds, rate=rate, spread=spread });
    }

    public void Advance(float seconds)
    {
        for (int s=streams.Count-1; s>=0; s--)
        {
            var stream = streams[s];
            stream.owed += stream.rate*Mathf.Min(seconds,stream.left);
            float drop = Mathf.Max(.02f,stream.from.y-stream.to.y);
            float life = Mathf.Sqrt(2f*drop/9.81f);
            Vector3 drift = (stream.to-stream.from)/life;
            drift.y = 0;
            while (stream.owed>=1f)
            {
                stream.owed -= 1f;
                Vector2 jitter = Random.insideUnitCircle*stream.spread;
                Grain(stream.from+new Vector3(jitter.x,0,jitter.y)*.25f,
                    drift+new Vector3(jitter.x,0,jitter.y)/life,life);
            }
            stream.left -= seconds;
            if (stream.left<=0) streams.RemoveAt(s); else streams[s] = stream;
        }
    }
}
