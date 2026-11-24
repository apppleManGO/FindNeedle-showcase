using UnityEngine;

// One ball travels between the outside court and the playable sand. In co-op it is the host's ball: the host
// moves it, decides who catches it and who it hits, and tells everyone where it is. Guests ask to catch and say
// where they threw it, and in between only watch it.
public sealed class BeachVolleyball : MonoBehaviour
{
    const float Radius = .14f;
    const float Reach = .36f;
    BeachSearch game;
    Transform ball;
    public Transform Model => ball;
    Vector3 serveOrigin, velocity;
    float delay, askedAt = -9, sendDue;
    bool inFlight, dangerous;
    // Guest: where the host last said the ball is.
    Vector3 heard;
    bool heardAny;
    // The player holding the ball, -1 when nobody is. Another player's ball is drawn in their figure's hands.
    public int Holder { get; private set; } = -1;
    public bool Held => Holder>=0 && Holder==game.MyId;
    bool Shared => game.Outbox!=null;
    bool Watching => Shared && game.Guest;

    public void Initialize(BeachSearch beach)
    {
        game = beach;
        float edge = game.sand.HalfSize.x;
        serveOrigin = new Vector3(edge + 1.6f, 1.55f, -.6f);
        CoastProp.Place("Net","배구 네트",transform,new Vector3(edge+.7f,0,-.6f),new Vector3(3f,1.7f,.08f),90);
        ball = new GameObject("비치발리볼").transform;
        ball.SetParent(transform,false);
        CoastProp.Place("Volleyball","배구공",ball,Vector3.down*Radius,Vector3.one*(2*Radius));
        ball.position = serveOrigin;
        delay = 2;
    }

    public bool WithinReach(Ray aim)
    {
        if (Holder>=0 || !ball.gameObject.activeSelf) return false;
        Vector3 offset = ball.position - aim.origin;
        float ahead = Vector3.Dot(offset,aim.direction);
        return ahead > 0 && offset.magnitude <= 1.95f &&
            (offset - aim.direction*ahead).magnitude <= .24f + .12f*ahead &&
            !Physics.Linecast(aim.origin,ball.position);
    }

    // True when the ball may be taken now. A guest asks the host instead, and the answer arrives later.
    public bool TryCatch()
    {
        if (Holder>=0) return false;
        if (!Watching) return true;
        if (Time.time-askedAt < .6f) return false;
        askedAt = Time.time;
        game.AskForBall();
        return false;
    }

    public void Catch()
    {
        Grab(game.MyId);
        if (Shared && !game.Guest) game.AnnounceBallHolder(game.MyId);
    }

    // `who` has the ball. This player's own ball sits in their view; anyone else's is drawn by their figure.
    public void Grab(int who)
    {
        Holder = who;
        inFlight = dangerous = false;
        askedAt = -9;
        if (who==game.MyId)
        {
            ball.gameObject.SetActive(true);
            ball.SetParent(game.player.view.transform,false);
            ball.localPosition = new Vector3(0,-.17f,.49f);
        }
        else
        {
            ball.SetParent(transform,true);
            ball.gameObject.SetActive(false);
        }
    }

    public void Throw(Vector3 direction)
    {
        Vector3 from = ball.position;
        if (Watching)
        {
            // The host throws it for us; until its word arrives the ball waits where it left the hands.
            Free();
            ball.position = heard = from;
            game.ThrowBall(from,direction);
            return;
        }
        Launch(from,direction);
    }

    void Free()
    {
        Holder = -1;
        ball.SetParent(transform,true);
        ball.gameObject.SetActive(true);
    }

    void Launch(Vector3 from,Vector3 direction)
    {
        Free();
        ball.position = from;
        inFlight = true;
        dangerous = false;
        velocity = direction.normalized*7f + Vector3.up*1.4f;
    }

    // ---- Host ----

    public void Requested(int who)
    {
        if (Holder>=0) return;
        Grab(who);
        game.AnnounceBallHolder(who);
    }

    public void ThrownBy(int who,Vector3 from,Vector3 direction)
    {
        if (Holder==who) Launch(from,direction);
    }

    // A player left the beach. If they had the ball it goes back to the court.
    public void Left(int who)
    {
        if (Holder!=who) return;
        Free();
        ball.position = serveOrigin;
        Rest(3);
    }

    // ---- Guest ----

    public void Heard(Vector3 position,int holder)
    {
        // This player's own catch and throw come by their own messages, so a late word about them is ignored.
        if (holder==game.MyId || Held) return;
        heard = position;
        heardAny = true;
        if (holder>=0) { if (Holder!=holder) Grab(holder); return; }
        if (Holder>=0) { Free(); ball.position = position; }
    }

    // The session ended. A ball in someone else's hands, or one only watched, goes back to the court.
    public void Unshare()
    {
        heardAny = false;
        if (Held) return;
        Free();
        ball.position = serveOrigin;
        Rest(3);
    }

    void Update()
    {
        if (!game || !Shared) return;
        if (game.Guest)
        {
            if (!heardAny || Holder>=0) return;
            ball.position = Vector3.Lerp(ball.position,heard,1-Mathf.Exp(-16f*Time.deltaTime));
            return;
        }
        // The host's ball keeps going while the host is in a menu, since the others are still playing.
        if (game.Started && !game.Won) Advance(Time.deltaTime);
        sendDue -= Time.unscaledDeltaTime;
        if (sendDue>0) return;
        sendDue = 1f/15f;
        game.SendBall(ball.position,Holder);
    }

    public void Advance(float dt)
    {
        if (Holder>=0) return;
        if (!inFlight)
        {
            delay -= dt;
            if (delay > 0) return;
            ball.position = serveOrigin;
            velocity = new Vector3(-3f,3.3f,Random.Range(-.75f,.75f));
            inFlight = dangerous = true;
            game.BallServed();
            return;
        }
        while (dt > 0 && inFlight)
        {
            float step = Mathf.Min(dt,1f/60);
            dt -= step;
            MoveBall(step);
        }
    }

    void MoveBall(float dt)
    {
        velocity += Physics.gravity * dt;
        Vector3 movement = velocity*dt;
        Vector3 position = ball.position;
        if (Physics.SphereCast(position,Radius,movement.normalized,out var hit,movement.magnitude,
            Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
        {
            position += movement.normalized*Mathf.Max(0,hit.distance-.002f);
            velocity = Vector3.Reflect(velocity,hit.normal)*.48f;
        }
        else position += movement;

        Vector2 point = new Vector2(position.x,position.z);
        if (!game.sand.Contains(point,-2f))
        {
            ball.position = serveOrigin;
            Rest(3);
            return;
        }
        float ground = game.sand.Contains(point)
            ? game.sand.Height(new Vector2(position.x,position.z)) : 0;
        if (position.y <= ground+Radius)
        {
            position.y = ground+Radius;
            dangerous = false;
            if (Mathf.Abs(velocity.y)<.9f) Rest(15);
            else velocity = new Vector3(velocity.x*.68f,Mathf.Abs(velocity.y)*.38f,velocity.z*.68f);
        }
        ball.position = position;
        ball.Rotate(Vector3.right,velocity.magnitude*120*dt,Space.World);
        if (!dangerous) return;
        // A served ball hurts whoever it reaches first, here or on another player's machine.
        float eye = game.player.view.transform.position.y-game.player.transform.position.y;
        if (Strikes(game.player.transform.position,position,eye))
        {
            game.HitByBall(game.MyId);
            return;
        }
        if (game.Others==null) return;
        foreach (var (id,feet) in game.Others())
            if (Strikes(feet,position,eye)) { game.HitByBall(id); return; }
    }

    bool Strikes(Vector3 feet,Vector3 position,float eye)
    {
        Vector3 body = feet;
        body.y = Mathf.Clamp(position.y,feet.y+.18f,feet.y+eye);
        if (Vector3.Distance(body,position)>=Reach) return false;
        dangerous = false;
        velocity *= -.32f;
        return true;
    }

    void Rest(float seconds)
    {
        inFlight = dangerous = false;
        velocity = Vector3.zero;
        delay = seconds;
    }

    void OnDestroy()
    {
        if (ball) Destroy(ball.gameObject);
    }
}
