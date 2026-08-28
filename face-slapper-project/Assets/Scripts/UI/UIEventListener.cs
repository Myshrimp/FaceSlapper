using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FaceSlapper.UI
{
    /// <summary>
    /// UGUI 事件封装：挂在任意可被射线检测的 UI 物体上（需带 Graphic），
    /// 把 PointerClick / 按下抬起 / 拖拽(Begin/Drag/End) / 滚轮 接口回调
    /// 转成普通 C# event，业务侧无需自行实现 IEventSystemHandler 接口。
    ///
    /// 用法示例：
    ///   UIEventListener.Get(button.gameObject).Clicked += data => Debug.Log("click");
    ///   UIEventListener.Get(icon.gameObject).Dragging += data => iconTransform.position = data.position;
    ///   UIEventListener.Get(list.gameObject).Scrolled  += delta => ScrollBy(delta.y);
    /// </summary>
    public class UIEventListener : MonoBehaviour,
        IPointerClickHandler, IPointerDownHandler, IPointerUpHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler,
        IScrollHandler
    {
        /// <summary>点击：按下后未发生拖拽、原地抬起时触发。</summary>
        public event Action<PointerEventData> Clicked;

        /// <summary>按下（无论后续是否拖拽都会触发）。</summary>
        public event Action<PointerEventData> Pressed;

        /// <summary>抬起（含拖拽结束后的抬起；纯点击场景 Released 先于 Clicked）。</summary>
        public event Action<PointerEventData> Released;

        /// <summary>拖拽开始：位移超过 EventSystem 像素阈值后触发，此后不再触发 Clicked。</summary>
        public event Action<PointerEventData> DragBegan;

        /// <summary>拖拽中：每帧回调，data.delta 为本帧位移，data.position 为当前屏幕坐标。</summary>
        public event Action<PointerEventData> Dragging;

        /// <summary>拖拽结束。</summary>
        public event Action<PointerEventData> DragEnded;

        /// <summary>滚轮滚动：参数为滚轮增量（上滑通常为 +1，下滑为 -1；y 为主轴）。</summary>
        public event Action<Vector2> Scrolled;

        /// <summary>获取物体上的监听器，不存在则自动添加。</summary>
        public static UIEventListener Get(GameObject go)
        {
            UIEventListener listener = go.GetComponent<UIEventListener>();
            if (listener == null) listener = go.AddComponent<UIEventListener>();
            return listener;
        }

        /// <summary>获取组件所在物体上的监听器，不存在则自动添加。</summary>
        public static UIEventListener Get(Component component) => Get(component.gameObject);

        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke(eventData);

        public void OnPointerDown(PointerEventData eventData) => Pressed?.Invoke(eventData);

        public void OnPointerUp(PointerEventData eventData) => Released?.Invoke(eventData);

        public void OnBeginDrag(PointerEventData eventData) => DragBegan?.Invoke(eventData);

        public void OnDrag(PointerEventData eventData) => Dragging?.Invoke(eventData);

        public void OnEndDrag(PointerEventData eventData) => DragEnded?.Invoke(eventData);

        public void OnScroll(PointerEventData eventData) => Scrolled?.Invoke(eventData.scrollDelta);
    }
}
