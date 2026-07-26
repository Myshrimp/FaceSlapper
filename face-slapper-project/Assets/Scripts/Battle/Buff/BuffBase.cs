using FaceSlapper.Core;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// Buff 基类：处理持续时间（Timer）、失效标记与上下文链的通用样板。
    /// 派生类通过重写 Duration / PowerMultiplier / OnUse 提供具体增益。
    /// </summary>
    public class BuffBase : IBuff, IPoolable
    {
        protected Timer _timer;
        protected IBuff _curBuff;
        protected string buffName;
        protected BuffComponent _owner;
        private bool _expired;

        /// <summary>持续秒数；&lt;=0 表示永久有效。</summary>
        public virtual float Duration => 0f;

        /// <summary>技能威力倍率（角色使用 Ability 时提供的增益）。</summary>
        public virtual float PowerMultiplier => 1f;

        public string BuffName => string.IsNullOrEmpty(buffName) ? GetType().Name : buffName;

        public virtual void OnAttach(BuffComponent owner)
        {
            _owner = owner;
            _expired = false;
            StartTimer();
        }

        public virtual void OnDetach()
        {
            ReleaseTimer();
            _owner = null;
        }

        /// <summary>角色使用技能时触发，默认空实现。</summary>
        public virtual void OnUse()
        {

        }

        public virtual bool IsValid()
        {
            return !_expired;
        }

        /// <summary>标记 Buff 失效（BuffComponent 会在下一帧将其移除）。</summary>
        protected void Expire() => _expired = true;

        /// <summary>刷新持续时间（重复获得同类 Buff 时调用）。</summary>
        public void Refresh()
        {
            _expired = false;
            if (_timer != null)
                _timer.Reset();
            else
                StartTimer();
        }

        private void StartTimer()
        {
            if (Duration <= 0f || !GameManager.HasInstance) return;
            TimeComponent time = GameManager.Instance.Get<TimeComponent>();
            if (time == null) return;
            _timer = time.CreateTimer(Duration, Expire);
        }

        private void ReleaseTimer()
        {
            if (_timer == null) return;
            _timer.Stop();
            if (GameManager.HasInstance)
            {
                TimeComponent time = GameManager.Instance.Get<TimeComponent>();
                if (time != null) time.RemoveTimer(_timer);
            }
            _timer = null;
        }

        public string[] GetContext()
        {
            string[] inner = _curBuff?.GetContext();
            if (inner == null || inner.Length == 0)
                return new[] { BuffName };

            string[] context = new string[inner.Length + 1];
            inner.CopyTo(context, 0);
            context[inner.Length] = BuffName;
            return context;
        }

        public virtual void OnGet()
        {
            _expired = false;
        }

        public virtual void OnReturn()
        {
            ReleaseTimer();
            _owner = null;
            _curBuff = null;
        }
    }
}
