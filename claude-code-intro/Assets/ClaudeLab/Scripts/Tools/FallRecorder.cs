using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ClaudeLab
{
    /// <summary>
    /// Added to every ball and block by spawn_object. Can hold the object frozen in mid-air until
    /// Release (so students see the drop start), and records each drop: from release (the Release button,
    /// release_objects, or a student letting go of it) to the first thing it hits. measure_object reads these numbers.
    /// Recording happens here, every physics step, because Claude's tool calls arrive a second or more
    /// after the object has already landed.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class FallRecorder : MonoBehaviour
    {
        public float floorY;

        [Tooltip("Start frozen in place and wait for Release() instead of falling straight away.")]
        public bool startWaiting;

        /// <summary>Frozen in mid-air, waiting for Release().</summary>
        public bool IsWaiting { get; private set; }
        /// <summary>A student is holding it with a controller or hand.</summary>
        public bool IsHeld { get; private set; }
        public bool HasDrop { get; private set; }
        public bool HasLanded { get; private set; }
        public float ReleaseHeight { get; private set; }
        public float PeakHeight { get; private set; }
        public float FallTime { get; private set; }
        public float ImpactSpeed { get; private set; }
        public int DropCount { get; private set; }

        Rigidbody _body;
        Collider _collider;
        float _releaseTime;

        public Rigidbody Body => _body;
        /// <summary>Height of the object's lowest point above the floor, matching spawn_object's height_m.</summary>
        public float Height => (_collider != null ? _collider.bounds.min.y : transform.position.y) - floorY;

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _collider = GetComponent<Collider>();
            var grab = GetComponent<XRGrabInteractable>();
            if (grab != null)
            {
                grab.selectEntered.AddListener(_ =>
                {
                    IsHeld = true;
                    Unfreeze(); // a student grabbed it before Release; it falls normally when they let go
                });
                grab.selectExited.AddListener(_ => { IsHeld = false; StartDrop(); });
            }
        }

        void Start()
        {
            if (startWaiting)
            {
                IsWaiting = true;
                // Constraints, not isKinematic: XRGrabInteractable saves and restores isKinematic and
                // useGravity around a grab, but leaves constraints alone.
                _body.constraints = RigidbodyConstraints.FreezeAll;
            }
            else
            {
                StartDrop();
            }
        }

        /// <summary>Let go of a waiting object. Returns false if it wasn't waiting.</summary>
        public bool Release()
        {
            if (!IsWaiting) return false;
            Unfreeze();
            StartDrop();
            return true;
        }

        void Unfreeze()
        {
            if (!IsWaiting) return;
            IsWaiting = false;
            _body.constraints = RigidbodyConstraints.None;
            _body.WakeUp();
        }

        void StartDrop()
        {
            HasDrop = true;
            HasLanded = false;
            DropCount++;
            _releaseTime = Time.time;
            ReleaseHeight = Height;
            PeakHeight = ReleaseHeight;
            FallTime = 0f;
            ImpactSpeed = 0f;
        }

        void FixedUpdate()
        {
            if (IsWaiting || IsHeld || HasLanded) return;
            PeakHeight = Mathf.Max(PeakHeight, Height);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (IsWaiting || IsHeld || HasLanded || !HasDrop) return;
            HasLanded = true;
            FallTime = Time.time - _releaseTime;
            ImpactSpeed = collision.relativeVelocity.magnitude;
        }
    }
}
