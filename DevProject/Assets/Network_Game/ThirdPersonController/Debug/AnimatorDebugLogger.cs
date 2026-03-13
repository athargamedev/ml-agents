using UnityEngine;

namespace Network_Game.ThirdPersonController.Debug
{
    /// <summary>
    /// Attach this to your player to log Animator state and Speed parameter in Play mode.
    /// </summary>
    public class AnimatorDebugLogger : MonoBehaviour
    {
        public Animator animator;
        public float logInterval = 0.5f;
        private float _timer;

        void Start()
        {
            if (animator == null)
                animator = GetComponent<Animator>();
        }

        void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= logInterval && animator != null)
            {
                _timer = 0f;
                float speed = animator.GetFloat("Speed");
                var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                string clipName = "";
                var clipInfos = animator.GetCurrentAnimatorClipInfo(0);
                if (clipInfos.Length > 0)
                {
                    clipName = clipInfos[0].clip.name;
                }
                UnityEngine.Debug.Log(
                    $"[AnimatorDebugLogger] Speed: {speed:F2}, Clip: {clipName}, NormalizedTime: {stateInfo.normalizedTime:F2}"
                );
            }
        }
    }
}
