using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FaceSlapper.UI
{
    /// <summary>
    /// UI 管理器：维护层级节点与面板栈，由 UIComponent 持有。
    ///
    /// 层级实现：每层一个子 Canvas（overrideSorting，sortingOrder = UILayer 枚举值），
    /// 层与层之间绝不穿插；同层内后打开的面板 SetAsLastSibling 置顶。
    ///
    /// 面板按具体类型唯一：同类型面板重复 Open 直接返回已有实例。
    /// </summary>
    public class UIManager
    {
        private readonly RectTransform _root;
        private readonly Dictionary<UILayer, RectTransform> _layers = new Dictionary<UILayer, RectTransform>(8);
        private readonly Dictionary<Type, UIPanel> _panels = new Dictionary<Type, UIPanel>(16);
        private readonly List<UIPanel> _openStack = new List<UIPanel>(16);

        public UIManager(RectTransform root) => _root = root;

        /// <summary>当前位于栈顶（最上层、最后打开）的面板，无则为 null。</summary>
        public UIPanel TopPanel => _openStack.Count > 0 ? _openStack[_openStack.Count - 1] : null;

        /// <summary>当前打开的面板数量。</summary>
        public int OpenCount => _openStack.Count;

        /// <summary>获取（惰性创建）指定层级的挂载节点。</summary>
        public RectTransform GetLayerNode(UILayer layer)
        {
            if (_layers.TryGetValue(layer, out RectTransform node) && node != null)
                return node;

            // 注意：GraphicRaycaster 必须挂在每个层级子 Canvas 上——
            // uGUI 按"最近的 Canvas"注册 Graphic 的射线目标，根 Canvas 上的
            // GraphicRaycaster 检测不到嵌套子 Canvas 里的图形。
            GameObject go = new GameObject($"Layer_{layer}", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            node = (RectTransform)go.transform;
            node.SetParent(_root, false);
            node.anchorMin = Vector2.zero;
            node.anchorMax = Vector2.one;
            node.offsetMin = Vector2.zero;
            node.offsetMax = Vector2.zero;
            node.localScale = Vector3.one;

            // 子 Canvas 覆盖排序：枚举值即 sortingOrder，保证层级不穿插。
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = (int)layer;

            _layers[layer] = node;
            return node;
        }

        /// <summary>从预制体打开面板：实例化到其声明的层级下。已打开时直接返回现有实例。</summary>
        public T Open<T>(T prefab) where T : UIPanel
        {
            T existing = Get<T>();
            if (existing != null) return existing;

            T panel = UnityEngine.Object.Instantiate(prefab, GetLayerNode(prefab.Layer), false);
            panel.name = prefab.name;
            RegisterOpen(panel);
            return panel;
        }

        /// <summary>打开场景中已存在的面板实例：重挂到其声明的层级下并打开。</summary>
        public T OpenInstance<T>(T panel) where T : UIPanel
        {
            T existing = Get<T>();
            if (existing != null)
            {
                if (existing != panel)
                    Debug.LogWarning($"[UIManager] {typeof(T).Name} 已打开，忽略新的实例。");
                return existing;
            }

            panel.transform.SetParent(GetLayerNode(panel.Layer), false);
            RegisterOpen(panel);
            return panel;
        }

        /// <summary>获取已打开的面板，未打开返回 null。</summary>
        public T Get<T>() where T : UIPanel
        {
            return _panels.TryGetValue(typeof(T), out UIPanel panel) && panel != null ? (T)panel : null;
        }

        /// <summary>面板是否已打开。</summary>
        public bool IsOpen<T>() where T : UIPanel => Get<T>() != null;

        /// <summary>关闭指定类型的面板。未打开返回 false。</summary>
        public bool Close<T>() where T : UIPanel
        {
            T panel = Get<T>();
            if (panel == null) return false;
            Close(panel);
            return true;
        }

        /// <summary>关闭指定面板实例（销毁其 GameObject）。</summary>
        public void Close(UIPanel panel)
        {
            if (panel == null || !_openStack.Contains(panel)) return;

            panel.CloseInternal();
            _panels.Remove(panel.GetType());
            _openStack.Remove(panel);
            if (panel != null) UnityEngine.Object.Destroy(panel.gameObject);
        }

        /// <summary>关闭全部面板（按打开顺序反向关闭）。</summary>
        public void CloseAll()
        {
            for (int i = _openStack.Count - 1; i >= 0; i--)
            {
                UIPanel panel = _openStack[i];
                panel.CloseInternal();
                if (panel != null) UnityEngine.Object.Destroy(panel.gameObject);
            }
            _panels.Clear();
            _openStack.Clear();
        }

        /// <summary>登记并打开面板实例：置为同层最上、压入面板栈。</summary>
        private void RegisterOpen(UIPanel panel)
        {
            _panels[panel.GetType()] = panel;
            _openStack.Remove(panel);
            _openStack.Add(panel);
            panel.transform.SetAsLastSibling();
            panel.OpenInternal();
        }
    }
}
