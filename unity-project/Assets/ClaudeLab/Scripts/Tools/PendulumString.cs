using UnityEngine;

namespace ClaudeLab
{
    /// <summary>Keeps a pendulum's string drawn between pivot and bob.</summary>
    [RequireComponent(typeof(LineRenderer))]
    public class PendulumString : MonoBehaviour
    {
        public Transform pivot;
        LineRenderer _line;

        void Awake() => _line = GetComponent<LineRenderer>();

        void LateUpdate()
        {
            if (pivot == null) return;
            _line.SetPosition(0, pivot.position);
            _line.SetPosition(1, transform.position);
        }
    }
}
