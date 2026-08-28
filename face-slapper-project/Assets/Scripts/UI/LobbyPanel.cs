using System;
using FaceSlapper.Core;
using FaceSlapper.Networking;
using FaceSlapper.Room;
using UnityEngine;
using UnityEngine.UI;

namespace FaceSlapper.UI
{
    /// <summary>
    /// 大厅面板：纯代码构建的 uGUI 界面（无预制体依赖，与项目程序化资产风格一致）。
    /// 提供 创建主机 / 加入房间 / 断开 / 开始游戏 入口与房间玩家列表。
    ///
    /// 同时作为 UI 框架事件封装的示范用法：
    ///   - 按钮点击与按下反馈 走 UIEventListener.Clicked / Pressed / Released；
    ///   - 标题栏拖拽移动面板   走 UIEventListener.Dragging；
    ///   - 玩家列表滚轮滚动     走 UIEventListener.Scrolled（不用 ScrollRect）。
    ///
    /// 房间状态进入 Playing 后面板自动关闭。
    /// </summary>
    public class LobbyPanel : UIPanel
    {
        // ---------------- 样式 ----------------

        private static readonly Color BgColor = new Color(0.13f, 0.13f, 0.16f, 0.98f);
        private static readonly Color TitleBarColor = new Color(0.20f, 0.20f, 0.25f, 1f);
        private static readonly Color InputBgColor = new Color(0.09f, 0.09f, 0.11f, 1f);
        private static readonly Color ListBgColor = new Color(0.10f, 0.10f, 0.13f, 1f);
        private static readonly Color HostBtnColor = new Color(0.25f, 0.45f, 0.85f, 1f);
        private static readonly Color JoinBtnColor = new Color(0.25f, 0.60f, 0.40f, 1f);
        private static readonly Color StopBtnColor = new Color(0.72f, 0.30f, 0.30f, 1f);
        private static readonly Color StartBtnColor = new Color(0.85f, 0.55f, 0.20f, 1f);
        private static readonly Color BtnDisabledColor = new Color(0.35f, 0.35f, 0.38f, 0.6f);
        private static readonly Color LocalPlayerColor = new Color(1f, 0.9f, 0.4f, 1f);

        private const float ScrollStep = 40f;      // 滚轮每格滚动的像素数
        private const float RefreshInterval = 0.25f; // 状态轮询间隔（秒）

        private static Font s_font;

        /// <summary>内置字体（2022 起 Arial 已替换为 LegacyRuntime，动态字体支持中文回退）。</summary>
        private static Font DefaultFont
        {
            get
            {
                if (s_font == null)
                {
                    s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (s_font == null) s_font = Font.CreateDynamicFontFromOSFont("Arial", 16);
                }
                return s_font;
            }
        }

        // ---------------- 运行状态 ----------------

        private RectTransform _rect;
        private Text _statusText;
        private InputField _ipInput;
        private RectTransform _listViewport;
        private RectTransform _listContent;
        private float _scrollOffset;
        private float _refreshTimer;

        private UIButton _btnHost;
        private UIButton _btnJoin;
        private UIButton _btnStop;
        private UIButton _btnStart;

        /// <summary>当前已绑定 Players.OnChange 的房间对象。</summary>
        private RoomComponent _boundRoom;

        /// <summary>打开大厅面板（已打开则返回现有实例）。</summary>
        public static LobbyPanel Open()
        {
            if (!GameManager.HasInstance) return null;
            UIComponent ui = GameManager.Instance.Get<UIComponent>();
            if (ui == null || ui.Manager == null) return null;

            LobbyPanel existing = ui.Manager.Get<LobbyPanel>();
            if (existing != null) return existing;

            var go = new GameObject("LobbyPanel", typeof(RectTransform), typeof(LobbyPanel));
            return ui.Manager.OpenInstance(go.GetComponent<LobbyPanel>());
        }

        // ---------------- 生命周期 ----------------

        protected override void OnOpen()
        {
            BuildUI();

            Net.OnClientStarted += RefreshStatus;
            Net.OnClientStopped += RefreshStatus;
            Net.OnServerStarted += RefreshStatus;
            Net.OnServerStopped += RefreshStatus;
            EventBus.Subscribe<RoomStateChangedEvent>(OnRoomStateChanged);

            RefreshRoomBinding();
            RefreshStatus();
        }

        protected override void OnClose()
        {
            Net.OnClientStarted -= RefreshStatus;
            Net.OnClientStopped -= RefreshStatus;
            Net.OnServerStarted -= RefreshStatus;
            Net.OnServerStopped -= RefreshStatus;
            EventBus.Unsubscribe<RoomStateChangedEvent>(OnRoomStateChanged);

            if (_boundRoom != null) _boundRoom.Players.OnChange -= RebuildPlayerList;
            _boundRoom = null;
        }

        /// <summary>低频轮询：兜底房间对象的懒绑定与状态刷新（网络对象生成没有客户端事件）。</summary>
        private void Update()
        {
            _refreshTimer += Time.deltaTime;
            if (_refreshTimer < RefreshInterval) return;
            _refreshTimer = 0f;
            RefreshRoomBinding();
            RefreshStatus();
        }

        // ---------------- 事件处理 ----------------

        private void OnRoomStateChanged(RoomStateChangedEvent evt)
        {
            RefreshStatus();
            // 游戏开始后大厅使命完成，自动关闭。
            if (evt.State == RoomState.Playing) Close();
        }

        private void OnHostClicked()
        {
            GameManager.Instance.Get<RoomHandler>().Host();
            RefreshStatus();
        }

        private void OnJoinClicked()
        {
            string ip = _ipInput.text.Trim();
            if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";
            GameManager.Instance.Get<RoomHandler>().Join(ip);
            RefreshStatus();
        }

        private void OnStopClicked()
        {
            GameManager.Instance.Get<RoomHandler>().Stop();
            RefreshStatus();
        }

        private void OnStartClicked()
        {
            GameManager.Instance.Get<RoomHandler>().StartGame();
        }

        // ---------------- 状态刷新 ----------------

        /// <summary>绑定/换绑房间玩家列表的变化事件（RoomComponent 连接后才生成）。</summary>
        private void RefreshRoomBinding()
        {
            RoomHandler handler = GameManager.Instance.Get<RoomHandler>();
            RoomComponent room = handler != null ? handler.Room : null;
            if (room == _boundRoom) return;

            if (_boundRoom != null) _boundRoom.Players.OnChange -= RebuildPlayerList;
            _boundRoom = room;
            if (_boundRoom != null) _boundRoom.Players.OnChange += RebuildPlayerList;
            RebuildPlayerList();
        }

        private void RefreshStatus()
        {
            if (_statusText == null) return;

            bool connected = Net.IsServer || Net.IsClient;
            string status;
            if (!connected) status = "未连接 — 创建主机或输入 IP 加入房间";
            else if (Net.IsHost) status = "主机运行中（本机即服务器）";
            else if (Net.IsServer) status = "服务器运行中";
            else status = "已连接到服务器";

            RoomComponent room = _boundRoom;
            if (connected && room != null)
            {
                status += room.State == RoomState.Lobby
                    ? $"  ·  大厅等待中（{room.Players.Count} 人）"
                    : "  ·  游戏进行中";
            }
            _statusText.text = status;

            _btnHost.SetEnabled(!connected);
            _btnJoin.SetEnabled(!connected);
            _btnStop.SetEnabled(connected);
            _btnStart.SetEnabled(connected && room != null && room.State == RoomState.Lobby);
        }

        /// <summary>重建玩家列表行（NetList 任意变化时回调）。</summary>
        private void RebuildPlayerList()
        {
            if (_listContent == null) return;

            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);

            RoomComponent room = _boundRoom;
            if (room == null || room.Players.Count == 0)
            {
                CreateListRow("（暂无玩家）", new Color(1f, 1f, 1f, 0.4f));
            }
            else
            {
                for (int i = 0; i < room.Players.Count; i++)
                {
                    RoomPlayerInfo info = room.Players[i];
                    bool isLocal = info.ClientId == Net.LocalClientId;
                    string line = $"{info.PlayerName}  #{info.ClientId}" + (isLocal ? "（我）" : string.Empty);
                    CreateListRow(line, isLocal ? LocalPlayerColor : Color.white);
                }
            }

            // 立即刷新布局并重算滚动范围，回到顶部。
            Canvas.ForceUpdateCanvases();
            _scrollOffset = 0f;
            ApplyScroll();
        }

        // ---------------- 滚轮滚动 ----------------

        private void OnListScrolled(Vector2 delta)
        {
            // 滚轮下滑 delta.y < 0 → 内容上移显示下方条目。
            _scrollOffset -= delta.y * ScrollStep;
            ApplyScroll();
        }

        private void ApplyScroll()
        {
            float viewportH = _listViewport.rect.height;
            float contentH = _listContent.rect.height;
            float max = Mathf.Max(0f, contentH - viewportH);
            _scrollOffset = Mathf.Clamp(_scrollOffset, 0f, max);
            _listContent.anchoredPosition = new Vector2(0f, _scrollOffset);
        }

        // ---------------- 界面构建 ----------------

        private void BuildUI()
        {
            _rect = (RectTransform)transform;
            // 面板本体：居中 460×620。
            _rect.anchorMin = new Vector2(0.5f, 0.5f);
            _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0.5f, 0.5f);
            _rect.anchoredPosition = Vector2.zero;
            _rect.sizeDelta = new Vector2(460f, 620f);

            Image bg = gameObject.AddComponent<Image>();
            bg.color = BgColor;

            BuildTitleBar();
            _statusText = CreateText(transform, string.Empty, 16, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.85f));
            SetTop(_statusText.rectTransform, 54f, 28f);
            SetOffsets(_statusText.rectTransform, 12f, 12f, float.NaN, float.NaN); // 仅改左右
            BuildIpRow();
            BuildButtonRow();
            BuildStartButton();
            BuildList();
        }

        /// <summary>标题栏：标题文字 + 关闭按钮；整条可拖拽移动面板。</summary>
        private void BuildTitleBar()
        {
            GameObject bar = NewUI("TitleBar", transform);
            SetTop((RectTransform)bar.transform, 0f, 44f);
            Image img = bar.AddComponent<Image>();
            img.color = TitleBarColor;

            Text title = CreateText(bar.transform, "FaceSlapper 大厅", 20, TextAnchor.MiddleLeft, Color.white);
            SetOffsets(title.rectTransform, 14f, 50f, 0f, 0f);

            // 关闭按钮（右上角）。
            GameObject closeGo = NewUI("Btn_Close", bar.transform);
            RectTransform closeRt = (RectTransform)closeGo.transform;
            closeRt.anchorMin = new Vector2(1f, 0.5f);
            closeRt.anchorMax = new Vector2(1f, 0.5f);
            closeRt.pivot = new Vector2(1f, 0.5f);
            closeRt.anchoredPosition = new Vector2(-6f, 0f);
            closeRt.sizeDelta = new Vector2(36f, 30f);
            Image closeImg = closeGo.AddComponent<Image>();
            closeImg.color = StopBtnColor;
            Text closeText = CreateText(closeGo.transform, "×", 20, TextAnchor.MiddleCenter, Color.white);
            Stretch(closeText.rectTransform);
            UIEventListener.Get(closeGo).Clicked += _ => Close();

            // 标题栏拖拽移动面板（屏幕像素 ÷ 根 Canvas 缩放 = 面板坐标位移）。
            UIEventListener.Get(bar).Dragging += data =>
            {
                float scale = 1f;
                Canvas[] canvases = GetComponentsInParent<Canvas>();
                if (canvases.Length > 0) scale = Mathf.Max(0.01f, canvases[canvases.Length - 1].scaleFactor);
                _rect.anchoredPosition += data.delta / scale;
            };
        }

        /// <summary>IP 输入行：标签 + 输入框。</summary>
        private void BuildIpRow()
        {
            GameObject row = NewUI("IpRow", transform);
            SetTop((RectTransform)row.transform, 92f, 40f);
            SetOffsets((RectTransform)row.transform, 14f, 14f, float.NaN, float.NaN);

            Text label = CreateText(row.transform, "主机 IP", 17, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.7f));
            RectTransform labelRt = label.rectTransform;
            labelRt.anchorMin = new Vector2(0f, 0f);
            labelRt.anchorMax = new Vector2(0f, 1f);
            labelRt.pivot = new Vector2(0f, 0.5f);
            labelRt.anchoredPosition = Vector2.zero;
            labelRt.sizeDelta = new Vector2(66f, 0f);

            _ipInput = CreateInput(row.transform, "127.0.0.1", "输入主机 IP 地址");
            RectTransform inputRt = (RectTransform)_ipInput.transform;
            inputRt.anchorMin = new Vector2(0f, 0f);
            inputRt.anchorMax = new Vector2(1f, 1f);
            inputRt.offsetMin = new Vector2(74f, 0f);
            inputRt.offsetMax = Vector2.zero;
        }

        /// <summary>按钮行：创建主机 / 加入房间 / 断开。</summary>
        private void BuildButtonRow()
        {
            GameObject row = NewUI("ButtonRow", transform);
            SetTop((RectTransform)row.transform, 144f, 44f);
            SetOffsets((RectTransform)row.transform, 14f, 14f, float.NaN, float.NaN);

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            _btnHost = CreateButton(row.transform, "创建主机", HostBtnColor, OnHostClicked);
            _btnJoin = CreateButton(row.transform, "加入房间", JoinBtnColor, OnJoinClicked);
            _btnStop = CreateButton(row.transform, "断开", StopBtnColor, OnStopClicked);
        }

        /// <summary>开始游戏按钮（独占一行）。</summary>
        private void BuildStartButton()
        {
            GameObject row = NewUI("StartRow", transform);
            SetTop((RectTransform)row.transform, 198f, 46f);
            SetOffsets((RectTransform)row.transform, 14f, 14f, float.NaN, float.NaN);

            _btnStart = CreateButton(row.transform, "开始游戏", StartBtnColor, OnStartClicked);
            Stretch(((RectTransform)_btnStart.Bg.transform));
        }

        /// <summary>玩家列表：标题 + 带遮罩的视口，视口内滚轮滚动。</summary>
        private void BuildList()
        {
            Text title = CreateText(transform, "玩家列表", 16, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.6f));
            SetTop(title.rectTransform, 256f, 24f);
            SetOffsets(title.rectTransform, 16f, 16f, float.NaN, float.NaN);

            // 视口：RectMask2D 裁剪溢出内容；滚轮事件挂在视口上。
            GameObject viewport = NewUI("ListViewport", transform);
            _listViewport = (RectTransform)viewport.transform;
            SetTop(_listViewport, 284f, 322f);
            SetOffsets(_listViewport, 14f, 14f, float.NaN, float.NaN);
            Image viewportImg = viewport.AddComponent<Image>();
            viewportImg.color = ListBgColor;
            viewport.AddComponent<RectMask2D>();
            UIEventListener.Get(viewport).Scrolled += OnListScrolled;

            // 内容容器：顶部对齐 + 垂直布局 + 高度自适应。
            GameObject content = NewUI("ListContent", _listViewport);
            _listContent = (RectTransform)content.transform;
            _listContent.anchorMin = new Vector2(0f, 1f);
            _listContent.anchorMax = new Vector2(1f, 1f);
            _listContent.pivot = new Vector2(0.5f, 1f);
            _listContent.anchoredPosition = Vector2.zero;
            _listContent.sizeDelta = Vector2.zero;

            var vlayout = content.AddComponent<VerticalLayoutGroup>();
            vlayout.childAlignment = TextAnchor.UpperLeft;
            vlayout.childControlWidth = true;
            vlayout.childControlHeight = false;
            vlayout.childForceExpandWidth = true;
            vlayout.childForceExpandHeight = false;
            vlayout.spacing = 2f;
            vlayout.padding = new RectOffset(4, 4, 4, 4);

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        // ---------------- 控件工厂 ----------------

        /// <summary>简易按钮：Image 底 + 文字，点击/按下反馈全部走 UIEventListener 封装。</summary>
        private UIButton CreateButton(Transform parent, string label, Color color, Action onClick)
        {
            GameObject go = NewUI("Btn_" + label, parent);
            var btn = new UIButton();
            btn.Bg = go.AddComponent<Image>();
            btn.NormalColor = color;
            btn.OnClick = onClick;

            Text text = CreateText(go.transform, label, 18, TextAnchor.MiddleCenter, Color.white);
            Stretch(text.rectTransform);
            btn.Label = text;
            btn.SetEnabled(true);

            UIEventListener listener = UIEventListener.Get(go);
            listener.Clicked += _ => { if (btn.Enabled) btn.OnClick(); };
            listener.Pressed += _ => { if (btn.Enabled) btn.Bg.color = btn.NormalColor * 0.75f; };
            listener.Released += _ => { if (btn.Enabled) btn.Bg.color = btn.NormalColor; };
            return btn;
        }

        private InputField CreateInput(Transform parent, string defaultText, string placeholder)
        {
            GameObject go = NewUI("Input", parent);
            Image img = go.AddComponent<Image>();
            img.color = InputBgColor;

            Text text = CreateText(go.transform, defaultText, 17, TextAnchor.MiddleLeft, Color.white);
            text.supportRichText = false;
            text.raycastTarget = false;
            SetOffsets(text.rectTransform, 10f, 10f, 0f, 0f);

            Text ph = CreateText(go.transform, placeholder, 17, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.35f));
            ph.fontStyle = FontStyle.Italic;
            ph.raycastTarget = false;
            SetOffsets(ph.rectTransform, 10f, 10f, 0f, 0f);

            var input = go.AddComponent<InputField>();
            input.targetGraphic = img;
            input.textComponent = text;
            input.placeholder = ph;
            input.text = defaultText;
            return input;
        }

        private void CreateListRow(string content, Color color)
        {
            GameObject row = NewUI("Row", _listContent);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 30f;

            Text text = CreateText(row.transform, content, 17, TextAnchor.MiddleLeft, color);
            SetOffsets(text.rectTransform, 8f, 4f, 0f, 0f);
        }

        private Text CreateText(Transform parent, string content, int fontSize, TextAnchor anchor, Color color)
        {
            GameObject go = NewUI("Text", parent);
            var text = go.AddComponent<Text>();
            text.font = DefaultFont;
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false; // 文字不挡射线，保证父级按钮/视口收到事件
            return text;
        }

        private static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>顶部对齐：y 为距面板顶部的距离，height 为高度，宽度撑满。</summary>
        private static void SetTop(RectTransform rt, float y, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -y);
            rt.sizeDelta = new Vector2(0f, height);
        }

        /// <summary>在保持现有锚点的前提下调整边距；传 NaN 的维度保持不变。</summary>
        private static void SetOffsets(RectTransform rt, float left, float right, float bottom, float top)
        {
            Vector2 min = rt.offsetMin;
            Vector2 max = rt.offsetMax;
            if (!float.IsNaN(left)) min.x = left;
            if (!float.IsNaN(bottom)) min.y = bottom;
            if (!float.IsNaN(right)) max.x = -right;
            if (!float.IsNaN(top)) max.y = -top;
            rt.offsetMin = min;
            rt.offsetMax = max;
        }

        // ---------------- 内部类型 ----------------

        /// <summary>简易按钮状态（背景/文字/可用性），配合 UIEventListener 使用。</summary>
        private class UIButton
        {
            public Image Bg;
            public Text Label;
            public Color NormalColor;
            public Action OnClick;
            public bool Enabled { get; private set; }

            public void SetEnabled(bool value)
            {
                Enabled = value;
                Bg.color = value ? NormalColor : BtnDisabledColor;
                if (Label != null) Label.color = value ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            }
        }
    }
}
