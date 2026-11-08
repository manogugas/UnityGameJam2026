using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// A gate in the arena wall: a portcullis that slides up, a tunnel behind it where cars wait
    /// (spawnPoint) and a point inside the arena they drive to (entryPoint).
    /// The transform sits on the wall's inner face with forward pointing into the arena.
    /// </summary>
    public class ArenaGate : MonoBehaviour
    {
        public Transform portcullis;
        public Transform spawnPoint;
        public Transform entryPoint;
        public float openHeight = 5.3f;
        public float speed = 2.8f;

        /// <summary>A car is currently being let in.</summary>
        public bool Busy { get; set; }
        public bool IsOpen => open;
        public bool IsFullyOpen => portcullis == null || portcullis.localPosition.y >= closedPos.y + openHeight - 0.05f;
        public bool IsFullyClosed => portcullis == null || portcullis.localPosition.y <= closedPos.y + 0.05f;

        Rigidbody portcullisBody;
        Vector3 closedPos;
        bool open;

        void Awake()
        {
            if (portcullis == null) return;
            closedPos = portcullis.localPosition;
            portcullisBody = portcullis.GetComponent<Rigidbody>();
        }

        public void Open() => open = true;
        public void Close() => open = false;

        /// <summary>True once a point is this many metres inside the arena past the gate.</summary>
        public bool HasPassed(Vector3 worldPoint, float metres) =>
            Vector3.Dot(worldPoint - transform.position, transform.forward) > metres;

        void FixedUpdate()
        {
            if (portcullis == null) return;
            Vector3 target = closedPos + Vector3.up * (open ? openHeight : 0f);
            Vector3 next = Vector3.MoveTowards(portcullis.localPosition, target, speed * Time.fixedDeltaTime);
            if (portcullisBody != null && portcullis.parent != null)
                portcullisBody.MovePosition(portcullis.parent.TransformPoint(next));
            else
                portcullis.localPosition = next;
        }
    }
}
