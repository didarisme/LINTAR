using UnityEngine;
using UnityEngine.Events;

namespace LemiGame
{
    [RequireComponent(typeof(Collider))]
    public class FireSpreadTrigger : MonoBehaviour
    {
        public FireSpreadController controller;
        public UnityEvent<Collider> onFireEnter;
        public UnityEvent<Collider> onFireExit;
        public UnityEvent<Collider> onFireStay;

        private Collider cachedCollider;

        void Awake()
        {
            cachedCollider = GetComponent<Collider>();
        }

        void OnEnable()
        {
            if (controller != null && cachedCollider != null)
            {
                controller.RegisterCollider(cachedCollider);
                controller.OnFireEnter += HandleEnter;
                controller.OnFireExit += HandleExit;
                controller.OnFireStay += HandleStay;
            }
        }

        void OnDisable()
        {
            if (controller != null && cachedCollider != null)
            {
                controller.OnFireEnter -= HandleEnter;
                controller.OnFireExit -= HandleExit;
                controller.OnFireStay -= HandleStay;
                controller.UnregisterCollider(cachedCollider);
            }
        }

        private void HandleEnter(Collider col)
        {
            if (col == cachedCollider)
                onFireEnter?.Invoke(col);
        }

        private void HandleExit(Collider col)
        {
            if (col == cachedCollider)
                onFireExit?.Invoke(col);
        }

        private void HandleStay(Collider col)
        {
            if (col == cachedCollider)
                onFireStay?.Invoke(col);
        }
    }
}


