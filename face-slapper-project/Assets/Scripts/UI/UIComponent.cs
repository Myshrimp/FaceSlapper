using FaceSlapper.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FaceSlapper.UI
{
    /// <summary>
    /// UI 组件：创建全局 UIRoot（Canvas + CanvasScaler + GraphicRaycaster）与
    /// EventSystem（StandaloneInputModule，匹配旧版 Input Manager），均跨场景不销毁；
    /// 持有 UIManager 供业务访问。
    ///
    /// 用法：
    ///   UIManager ui = GameManager.Instance.Get&lt;UIComponent&gt;().Manager;
    ///   ui.Open(somePanelPrefab);            // 从预制体打开
    ///   ui.Close&lt;SomePanel&gt;();           // 关闭
    /// </summary>
    public class UIComponent : MonoBehaviour, IGameComponent
    {
        /// <summary>UI 根节点（ScreenSpaceOverlay Canvas）。</summary>
        public RectTransform Root { get; private set; }

        /// <summary>面板与层级管理器。</summary>
        public UIManager Manager { get; private set; }

        public void OnInit()
        {
            CreateRoot();
            CreateEventSystem();
            Manager = new UIManager(Root);
        }

        public void OnShutdown()
        {
            Manager?.CloseAll();
            Manager = null;
            if (Root != null) Destroy(Root.gameObject);
            Root = null;
        }

        /// <summary>创建（或复用场景中已有的）UIRoot。</summary>
        private void CreateRoot()
        {
            if (Root != null) return;

            GameObject go = GameObject.Find("UIRoot");
            if (go == null) go = new GameObject("UIRoot");

            Canvas canvas = go.GetComponent<Canvas>();
            if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // 射线检测集中在根 Canvas，层级子节点只负责排序，无需各自挂 Raycaster。
            if (go.GetComponent<GraphicRaycaster>() == null)
                go.AddComponent<GraphicRaycaster>();

            Root = (RectTransform)go.transform;
            DontDestroyOnLoad(go);
        }

        /// <summary>确保场景中存在 EventSystem（点击/拖拽/滚轮事件的分发入口）。</summary>
        private void CreateEventSystem()
        {
            if (EventSystem.current != null) return;

            GameObject go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(go);
        }
    }
}
