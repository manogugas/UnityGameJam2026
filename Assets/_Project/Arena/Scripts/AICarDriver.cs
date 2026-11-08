using UnityEngine;

namespace CarArena
{
    public enum DriverArchetype { Rammer, Flanker, Bruiser }

    /// <summary>
    /// Virtual driver for an enemy car. Drives the normal car physics through CarInput's override fields.
    ///  Rammer:  charges straight at a predicted intercept, backs off, lines up, charges again.
    ///  Flanker: circles to the target's side and T-bones the doors.
    ///  Bruiser: heavy and slower; after contact it keeps pushing (into walls) before backing off.
    /// All of them steer around obstacles, reverse out when stuck and flip themselves back upright.
    /// Targets: any living car (player or other enemies). Nearest wins, with a bias toward the player
    /// and toward whoever hit us last (revenge), and some stickiness so they don't flip-flop.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class AICarDriver : MonoBehaviour
    {
        public enum State { Hunt, Charge, Push, BackOff, Reposition, Unstuck, Idle }

        public DriverArchetype archetype = DriverArchetype.Rammer;
        public CarController target;

        [Header("Targeting")]
        public ArenaDirector director;
        [Tooltip("Metres subtracted from the player's distance when choosing a target (favour the player).")]
        public float playerBias = 12f;
        [Tooltip("Metres subtracted from the distance of whoever hit us last.")]
        public float revengeBias = 35f;
        public float revengeTime = 6f;
        public float retargetInterval = 1.5f;
        [Tooltip("A new target must be this many metres 'closer' than the current one to switch.")]
        public float switchMargin = 10f;

        [Header("Steering")]
        [Tooltip("Degrees of aim error that give full steering lock.")]
        public float fullLockAngle = 22f;
        [Tooltip("Max seconds of target movement to lead when aiming.")]
        public float maxLead = 1.4f;

        [Header("Obstacle avoidance")]
        public float avoidBaseDistance = 8f;
        public float avoidPerSpeed = 1.1f;
        public float avoidSpread = 28f;

        [Header("Arena")]
        [Tooltip("Reposition points are kept inside this radius from the arena centre.")]
        public float arenaRadius = 52f;

        public State CurrentState => state;
        public CarHealth TargetHealth => targetHealth;

        CarController car;
        CarInput input;
        CarHealth health;
        CarHealth targetHealth;
        Rigidbody rb;

        State state = State.Hunt;
        float stateTime, stuckTime, flipTime, backOffDuration;
        State afterBackOff = State.Reposition;
        float flankSide, personality;
        Vector3 repositionPoint;
        readonly RaycastHit[] hits = new RaycastHit[8];
        CarDamage damage;
        CarHealth lastAttacker;
        float lastAttackedTime = -100f, retargetTimer;

        // Per-archetype tuning, set in Awake.
        float huntThrottle, chargeThrottle, chargeDistance, flankRadius, pushTime, backOffTime;

        void Awake()
        {
            car = GetComponent<CarController>();
            input = GetComponent<CarInput>();
            health = GetComponent<CarHealth>();
            rb = GetComponent<Rigidbody>();
            damage = GetComponent<CarDamage>();
            input.overrideDriving = true;

            personality = Random.Range(0.85f, 1.15f);
            flankSide = Random.value < 0.5f ? -1f : 1f;
            switch (archetype)
            {
                case DriverArchetype.Flanker:
                    huntThrottle = 0.95f; chargeThrottle = 1f; chargeDistance = 22f; flankRadius = 14f; pushTime = 0f; backOffTime = 1.1f;
                    break;
                case DriverArchetype.Bruiser:
                    huntThrottle = 0.75f; chargeThrottle = 0.95f; chargeDistance = 26f; flankRadius = 0f; pushTime = 2.6f; backOffTime = 1.4f;
                    break;
                default:
                    huntThrottle = 0.85f; chargeThrottle = 1f; chargeDistance = 32f; flankRadius = 0f; pushTime = 0f; backOffTime = 1.2f;
                    break;
            }
            chargeDistance *= personality;
        }

        public void SetTarget(CarController newTarget)
        {
            target = newTarget;
            targetHealth = target != null ? target.GetComponent<CarHealth>() : null;
            Enter(State.Hunt);
        }

        void Start()
        {
            if (target != null && targetHealth == null) targetHealth = target.GetComponent<CarHealth>();
        }

        void OnEnable() { if (damage != null) damage.Impact += OnImpact; }
        void OnDisable() { if (damage != null) damage.Impact -= OnImpact; }

        void OnImpact(ImpactInfo info)
        {
            var attacker = info.otherBody != null ? info.otherBody.GetComponent<CarHealth>() : null;
            if (attacker == null || attacker == health || attacker.IsDead) return;
            lastAttacker = attacker;
            lastAttackedTime = Time.time;
            retargetTimer = Mathf.Min(retargetTimer, 0.2f); // react quickly to being hit
        }

        /// <summary>Pick the most attractive living car. Only switches mid-fight when clearly better.</summary>
        void ChooseTarget()
        {
            if (director == null) return;
            bool targetGone = targetHealth == null || targetHealth.IsDead;
            bool busy = state == State.Charge || state == State.Push || state == State.BackOff;
            if (busy && !targetGone) return;

            CarHealth best = null;
            float bestScore = float.MaxValue, currentScore = float.MaxValue;
            Vector3 myPos = rb.position;
            foreach (var c in director.Combatants)
            {
                if (c == null || c == health || c.IsDead) continue;
                float score = Flat(c.transform.position - myPos).magnitude;
                if (c.isPlayer) score -= playerBias;
                if (c == lastAttacker && Time.time - lastAttackedTime < revengeTime) score -= revengeBias;
                if (c == targetHealth) currentScore = score;
                if (score < bestScore) { bestScore = score; best = c; }
            }
            if (best == null) { if (targetGone) { target = null; targetHealth = null; } return; }
            if (targetGone || (best != targetHealth && bestScore < currentScore - switchMargin))
                SetTarget(best.car != null ? best.car : best.GetComponent<CarController>());
        }

        void Enter(State s)
        {
            state = s;
            stateTime = 0f;
            stuckTime = 0f;
            if (s == State.Reposition) repositionPoint = PickRepositionPoint();
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (health != null && health.IsDead) { Drive(0f, 0f, 0f); return; }

            FlipRecovery(dt);
            stateTime += dt;

            retargetTimer -= dt;
            if (retargetTimer <= 0f || (targetHealth != null && targetHealth.IsDead))
            {
                ChooseTarget();
                retargetTimer = retargetInterval * personality;
            }

            if (target == null || (targetHealth != null && targetHealth.IsDead))
            {
                if (state != State.Idle && state != State.Reposition) Enter(State.Reposition);
                if (state == State.Reposition && stateTime > 4f) Enter(State.Idle);
            }

            Vector3 myPos = rb.position;
            float speed = car.ForwardSpeed;
            Vector3 tPos = target != null ? target.Body.position : Vector3.zero;
            Vector3 tVel = target != null ? target.Body.linearVelocity : Vector3.zero;
            float dist = Flat(tPos - myPos).magnitude;
            float lead = Mathf.Clamp(dist / Mathf.Max(10f, rb.linearVelocity.magnitude + 6f), 0f, maxLead);
            Vector3 predicted = tPos + Flat(tVel) * lead;

            Vector3 aim = predicted;
            float throttle = 0f, brake = 0f;
            bool reverse = false;
            bool wantContact = false;

            switch (state)
            {
                case State.Hunt:
                    wantContact = true;
                    if (archetype == DriverArchetype.Flanker && target != null)
                    {
                        aim = FlankPoint(tPos);
                        Vector3 rel = target.transform.InverseTransformPoint(myPos);
                        bool onSide = Mathf.Abs(rel.x) > Mathf.Abs(rel.z) * 0.8f;
                        if (dist < chargeDistance && onSide) Enter(State.Charge);
                    }
                    else if (dist < chargeDistance) Enter(State.Charge);
                    throttle = huntThrottle;
                    break;

                case State.Charge:
                    wantContact = true;
                    throttle = chargeThrottle;
                    if (dist > chargeDistance * 1.8f) Enter(State.Hunt);
                    else if (stateTime > 6f) Enter(State.Reposition); // missed: go around and try again
                    break;

                case State.Push:
                    wantContact = true;
                    aim = tPos;
                    throttle = 1f;
                    if (stateTime > pushTime || dist > 7f) BackOff(backOffTime, State.Reposition);
                    break;

                case State.BackOff:
                    reverse = true;
                    if (stateTime > backOffDuration) Enter(afterBackOff);
                    break;

                case State.Reposition:
                    aim = repositionPoint;
                    throttle = 0.8f;
                    if (stateTime > 3f || Flat(repositionPoint - myPos).magnitude < 6f) Enter(target != null ? State.Hunt : State.Idle);
                    break;

                case State.Unstuck:
                    reverse = true;
                    if (stateTime > 1.3f) Enter(State.Hunt);
                    break;

                case State.Idle:
                    aim = Vector3.zero;
                    throttle = Flat(myPos).magnitude > 20f ? 0.45f : 0f;
                    brake = throttle > 0f ? 0f : 0.4f;
                    break;
            }

            float aimAngle = SignedAngleTo(aim);
            float steer;

            if (reverse)
            {
                // Back up while swinging the nose toward the target (reversing steers the other way).
                throttle = 0f;
                brake = 1f;
                steer = -Mathf.Sign(aimAngle) * 0.8f;
            }
            else
            {
                // Target behind us and we're slow: do a reversing turn instead of a huge circle.
                if (wantContact && Mathf.Abs(aimAngle) > 115f && Mathf.Abs(speed) < 6f && dist < 25f)
                {
                    BackOff(0.9f, State.Hunt);
                }

                steer = Mathf.Clamp(aimAngle / fullLockAngle, -1f, 1f);

                // Slow for tight turns.
                float a = Mathf.Abs(aimAngle);
                if (a > 50f && speed > 14f) throttle *= 0.35f;
                if (a > 75f && speed > 18f) { throttle = 0f; brake = 0.6f; }

                // Obstacles take over the steering in proportion to how close they are, so a target
                // hiding behind a block can't pull the car straight into it.
                Avoidance(speed, wantContact, out float avoidSteer, out float urgency, out float avoidBrake);
                steer = Mathf.Clamp(Mathf.Lerp(steer, avoidSteer, Mathf.Clamp01(urgency * 1.5f)), -1f, 1f);
                brake = Mathf.Max(brake, avoidBrake);
                if (avoidBrake > 0f) throttle *= 1f - avoidBrake;

                // Stuck: pushing on the throttle but not moving (against a wall, beached on a ramp...).
                if (throttle > 0.4f && Mathf.Abs(speed) < 1.2f && state != State.Push) stuckTime += dt;
                else stuckTime = Mathf.Max(0f, stuckTime - dt);
                if (stuckTime > 1.6f) Enter(State.Unstuck);
            }

            Drive(steer, throttle, brake);
        }

        void BackOff(float seconds, State then)
        {
            backOffDuration = seconds;
            afterBackOff = then;
            Enter(State.BackOff);
        }

        void Drive(float steer, float throttle, float brake)
        {
            input.overrideSteer = steer;
            input.overrideThrottle = Mathf.Clamp01(throttle);
            input.overrideBrake = Mathf.Clamp01(brake);
            input.overrideHandbrake = false;
        }

        void OnCollisionEnter(Collision c)
        {
            if (target == null || c.rigidbody != target.Body) return;
            if (state != State.Charge && state != State.Hunt) return;
            if (archetype == DriverArchetype.Bruiser) Enter(State.Push);
            else
            {
                BackOff(backOffTime, State.Reposition);
                if (archetype == DriverArchetype.Flanker) flankSide = -flankSide; // come in from the other side next
            }
        }

        Vector3 FlankPoint(Vector3 tPos)
        {
            Transform tt = target.transform;
            Vector3 right = Flat(tt.right).normalized;
            Vector3 fwd = Flat(tt.forward).normalized;
            return tPos + right * (flankSide * flankRadius) + fwd * 3f;
        }

        Vector3 PickRepositionPoint()
        {
            Vector3 myPos = rb.position;
            Vector3 away = target != null ? Flat(myPos - target.Body.position) : Flat(-myPos);
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
            away = Quaternion.Euler(0f, Random.Range(-50f, 50f), 0f) * away.normalized;
            Vector3 p = myPos + away * Random.Range(18f, 28f);
            p = Flat(p);
            if (p.magnitude > arenaRadius) p = p.normalized * arenaRadius * 0.8f;
            return p;
        }

        /// <summary>
        /// Looks ahead with a car-wide sphere sweep plus two angled rays. Returns the steer that avoids
        /// what was found, how urgent that is (0 clear, 1 at the bumper) and how hard to brake.
        /// </summary>
        void Avoidance(float speed, bool wantContact, out float steer, out float urgency, out float brake)
        {
            float length = avoidBaseDistance + Mathf.Max(0f, speed) * avoidPerSpeed;
            Vector3 origin = transform.TransformPoint(new Vector3(0f, 0.15f, 2.4f));
            float leftW = Probe(origin, -avoidSpread, length * 0.8f, 0.25f, wantContact);
            float centreW = Probe(origin, 0f, length, 0.6f, wantContact);
            float rightW = Probe(origin, avoidSpread, length * 0.8f, 0.25f, wantContact);

            steer = Mathf.Clamp((leftW - rightW) * 1.5f, -1f, 1f);
            if (centreW > 0f)
            {
                // Go round on the freer side; if both look the same, round the side the target is on.
                float side = leftW > rightW + 0.05f ? 1f : rightW > leftW + 0.05f ? -1f
                    : (target != null && SignedAngleTo(target.Body.position) < 0f ? -1f : 1f);
                steer = Mathf.Clamp(steer + side * (0.6f + centreW), -1f, 1f);
            }
            urgency = Mathf.Max(centreW, Mathf.Max(leftW, rightW) * 0.6f);
            brake = centreW > 0.3f && speed > 9f ? Mathf.Clamp01((centreW - 0.3f) * 1.8f) : 0f;
        }

        /// <summary>0 = clear, 1 = obstacle right at the bumper.</summary>
        float Probe(Vector3 origin, float yaw, float length, float radius, bool wantContact)
        {
            Vector3 dir = Quaternion.AngleAxis(yaw, transform.up) * transform.forward;
            int n = Physics.SphereCastNonAlloc(origin, radius, dir, hits, length, ~0, QueryTriggerInteraction.Ignore);
            float groundY = GroundHeight();
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.distance <= 0f) continue; // overlapping at the start (ground contact etc.)
                if (h.collider.attachedRigidbody == rb) continue;
                // Ground or the foot of a ramp we can drive up. An upward-facing hit higher up is the top
                // edge of a low obstacle (barrier, block) that the wide sweep reached over: that counts.
                if (h.normal.y > 0.55f && h.point.y - groundY < 0.45f) continue;
                if (wantContact && target != null && h.collider.attachedRigidbody == target.Body) continue;
                if (h.collider.attachedRigidbody != null && h.collider.attachedRigidbody.mass < 100f) continue; // tyres, crates, debris: plough through
                best = Mathf.Min(best, h.distance);
            }
            return best == float.MaxValue ? 0f : 1f - best / length;
        }

        /// <summary>Height of the ground under the car (average wheel contact), or a fixed offset in the air.</summary>
        float GroundHeight()
        {
            float sum = 0f;
            int n = 0;
            foreach (var w in car.wheels)
            {
                if (!w.Grounded) continue;
                sum += w.transform.position.y - (w.restLength - w.Compression) - w.radius;
                n++;
            }
            return n > 0 ? sum / n : transform.position.y - 0.65f;
        }

        void FlipRecovery(float dt)
        {
            bool upsideDown = Vector3.Dot(transform.up, Vector3.up) < 0.35f;
            bool stalled = rb.linearVelocity.sqrMagnitude < 4f;
            if (upsideDown && stalled) flipTime += dt;
            else flipTime = 0f;
            if (flipTime > 2.5f)
            {
                car.ResetCar();
                flipTime = 0f;
                Enter(State.Hunt);
            }
        }

        float SignedAngleTo(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            return Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
