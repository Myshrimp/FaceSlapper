using FaceSlapper.Core;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// Buff 基类：处理持续时间（按服务器权威 Timeline tick 倒计时）、失效标记与上下文链的通用样板。
    /// 派生类通过重写 Duration / PowerMultiplier / OnUse 提供具体增益。
    /// 倒计时由 BuffComponent 订阅 TimelineManager.Ticked 驱动，各端跟随同一权威 tick 流，天然对齐。
    /// </summary>
    public class BuffBase : IBuff, IPoolable
    {
        protected IBuff _curBuff;
        protected string buffName;
        protected BuffComponent _owner;
        private bool _expired;
        private int _remainingTicks;

        /// <summary>持续秒数；&lt;=0 表示永久有效。</summary>
        public virtual float Duration => 0f;

        /// <summary>持续 tick 数（按 GameSettings.TicksPerSec 换算）；&lt;=0 表示永久有效。</summary>
        public int DurationTicks => GameSettings.Sec2Ticks(Duration);

        /// <summary>剩余 tick 数（调试/界面显示用）。</summary>
        public int RemainingTicks => _remainingTicks;

        /// <summary>技能威力倍率（角色使用 Ability 时提供的增益）。</summary>
        public virtual float PowerMultiplier => 1f;

        public string BuffName => string.IsNullOrEmpty(buffName) ? GetType().Name : buffName;

        public virtual void OnAttach(BuffComponent owner)
        {
            _owner = owner;
            _expired = false;
            _remainingTicks = DurationTicks;
        }

        public virtual void OnDetach()
        {
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

        /// <summary>
        /// 主 Timeline 每推进一帧由 BuffComponent 调用一次；
        /// 倒计时归零时标记失效。永久 Buff（Duration &lt;= 0）不倒计时。
        /// </summary>
        internal void TickDown()
        {
            if (_expired || Duration <= 0f) return;
            if (--_remainingTicks <= 0) Expire();
        }

        /// <summary>刷新持续时间（重复获得同类 Buff 时调用）。</summary>
        public void Refresh()
        {
            _expired = false;
            _remainingTicks = DurationTicks;
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
            _remainingTicks = 0;
            _owner = null;
            _curBuff = null;
        }
    }
}
