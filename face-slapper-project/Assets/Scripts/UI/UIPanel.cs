using FaceSlapper.Core;
using UnityEngine;

namespace FaceSlapper.UI
{
    /// <summary>
    /// UI 面板基类。面板实例由 UIManager 统一管理：
    /// Open 时挂到所属层级节点下并置为同层最上，Close 时销毁 GameObject。
    /// 生命周期：OnOpen →（打开期间）→ OnClose → 销毁。
    /// </summary>
    public abstract class UIPanel : MonoBehaviour
    {
        [SerializeField] private UILayer _layer = UILayer.Normal;

        /// <summary>面板所属层级（Inspector 可配置），默认 Normal；子类也可重写该属性强制层级。</summary>
        public virtual UILayer Layer => _layer;

        /// <summary>是否处于打开状态。</summary>
        public bool IsOpen { get; private set; }

        /// <summary>关闭自身（等价于经 UIManager 关闭）。</summary>
        public void Close()
        {
            if (!GameManager.HasInstance) return;
            UIComponent ui = GameManager.Instance.Get<UIComponent>();
            if (ui != null && ui.Manager != null) ui.Manager.Close(this);
        }

        /// <summary>由 UIManager 调用，请勿直接使用。</summary>
        internal void OpenInternal()
        {
            IsOpen = true;
            gameObject.SetActive(true);
            OnOpen();
        }

        /// <summary>由 UIManager 调用，请勿直接使用。</summary>
        internal void CloseInternal()
        {
            OnClose();
            IsOpen = false;
        }

        /// <summary>面板打开时回调：在此刷新数据、订阅 EventBus 事件。</summary>
        protected virtual void OnOpen() { }

        /// <summary>面板关闭时回调：在此反订阅事件（对象随后会被销毁）。</summary>
        protected virtual void OnClose() { }
    }
}
