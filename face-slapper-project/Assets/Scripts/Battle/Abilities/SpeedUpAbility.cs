using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// 加速技能：按住加速键提升移速，有最长持续时间；松开或到时后进入冷却。
    /// 移动是客户端权威，因此只需本地改移速倍率即可全网生效。
    /// </summary>
    public class SpeedUpAbility : AbilityBase
    {
        [SerializeField] private float _coolDown = 4f;
        [SerializeField] private float _multiplier = 1.6f;
        [SerializeField] private float _maxDuration = 3f;

        private Movement _movement;
        private float _activeUntil;

        public bool IsActive { get; private set; }

        public override float GetCoolDown() => _coolDown;

        private void Awake()
        {
            _movement = GetComponent<Movement>();
        }

        public override void OnUse()
        {
            if (IsActive) return; // 已经按住加速中，不重复触发。
            base.OnUse();

            IsActive = true;
            _activeUntil = Time.time + _maxDuration;
            if (_movement != null) _movement.SetSpeedMultiplier(_multiplier);
        }

        public override void OnUseEnd()
        {
            if (!IsActive) return;
            IsActive = false;
            if (_movement != null) _movement.SetSpeedMultiplier(1f);
        }

        private void Update()
        {
            if (IsActive && Time.time >= _activeUntil)
                OnUseEnd();
        }
    }
}
