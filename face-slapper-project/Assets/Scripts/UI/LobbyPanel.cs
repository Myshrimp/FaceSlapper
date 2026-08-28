using FaceSlapper.Core;
using FaceSlapper.Networking;
using FaceSlapper.Room;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FaceSlapper.UI
{
    /// <summary>
    /// 大厅面板逻辑。界面由编辑器内手工组建的 uGUI 预制体提供，
    /// 本类只把控件引用接线到 RoomHandler / Net / EventBus 上。
    ///
    /// 预制体组建要求（根节点挂 LobbyPanel，以下字段在 Inspector 拖拽赋值）：
    ///   _statusText       状态栏文字（Text 或 TMP_Text 均不支持混用——本字段为 legacy Text）
    ///   _ipInput          主机 IP 输入框（InputField）
    ///   _hostButton       创建主机（Button）
    ///   _joinButton       加入房间（Button）
    ///   _stopButton       断开（Button）
    ///   _startButton      开始游戏（Button）
    ///   _closeButton      关闭面板（Button，可选）
    ///   _listContent      玩家列表容器（建议 VerticalLayoutGroup，置于 ScrollRect 内，滚轮由 ScrollRect 原生处理）
    ///   _playerRowPrefab  玩家行模板（需带 Text 或 TMP_Text，可选；为空则不渲染列表）
    ///   _dragHandle       标题栏拖拽手柄（挂 UIEventListener 的物体，可选；拖拽移动整个面板）
    ///
    /// 房间状态进入 Playing 后面板自动关闭。
    /// </summary>
    public class LobbyPanel : UIPanel
    {
        [SerializeField] private Text _statusText;
        [SerializeField] private InputField _ipInput;
        [SerializeField] private Button _hostButton;
        [SerializeField] private Button _joinButton;
        [SerializeField] private Button _stopButton;
        [SerializeField] private Button _startButton;
        [SerializeField] private Button _closeButton;
        [SerializeField] private RectTransform _listContent;
        [SerializeField] private GameObject _playerRowPrefab;
        [SerializeField] private UIEventListener _dragHandle;

        /// <summary>状态轮询间隔（秒）：兜底房间对象的懒绑定与状态刷新（网络对象生成没有客户端事件）。</summary>
        private const float RefreshInterval = 0.25f;

        private static readonly Color LocalPlayerColor = new Color(1f, 0.9f, 0.4f, 1f);

        private float _refreshTimer;

        /// <summary>当前已绑定 Players.OnChange 的房间对象。</summary>
        private RoomComponent _boundRoom;

        // ---------------- 生命周期 ----------------

        protected override void OnOpen()
        {
            WireButton(_hostButton, OnHostClicked, nameof(_hostButton));
            WireButton(_joinButton, OnJoinClicked, nameof(_joinButton));
            WireButton(_stopButton, OnStopClicked, nameof(_stopButton));
            WireButton(_startButton, OnStartClicked, nameof(_startButton));
            if (_closeButton != null) _closeButton.onClick.AddListener(Close);

            // 标题栏拖拽移动面板（可选）。
            if (_dragHandle != null) _dragHandle.Dragging += OnDragHandle;

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
            UnwireButton(_hostButton, OnHostClicked);
            UnwireButton(_joinButton, OnJoinClicked);
            UnwireButton(_stopButton, OnStopClicked);
            UnwireButton(_startButton, OnStartClicked);
            if (_closeButton != null) _closeButton.onClick.RemoveListener(Close);

            if (_dragHandle != null) _dragHandle.Dragging -= OnDragHandle;

            Net.OnClientStarted -= RefreshStatus;
            Net.OnClientStopped -= RefreshStatus;
            Net.OnServerStarted -= RefreshStatus;
            Net.OnServerStopped -= RefreshStatus;
            EventBus.Unsubscribe<RoomStateChangedEvent>(OnRoomStateChanged);

            if (_boundRoom != null) _boundRoom.Players.OnChange -= RebuildPlayerList;
            _boundRoom = null;
        }

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

        private void OnDragHandle(PointerEventData data)
        {
            // 屏幕像素 ÷ 根 Canvas 缩放 = 面板坐标位移。
            float scale = 1f;
            Canvas[] canvases = GetComponentsInParent<Canvas>();
            if (canvases.Length > 0) scale = Mathf.Max(0.01f, canvases[canvases.Length - 1].scaleFactor);
            ((RectTransform)transform).anchoredPosition += data.delta / scale;
        }

        private void OnHostClicked()
        {
            GameManager.Instance.Get<RoomHandler>().Host();
            RefreshStatus();
        }

        private void OnJoinClicked()
        {
            string ip = _ipInput != null ? _ipInput.text.Trim() : string.Empty;
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
            bool connected = Net.IsServer || Net.IsClient;

            if (_statusText != null)
            {
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
            }

            SetInteractable(_hostButton, !connected);
            SetInteractable(_joinButton, !connected);
            SetInteractable(_stopButton, connected);
            SetInteractable(_startButton, connected && _boundRoom != null && _boundRoom.State == RoomState.Lobby);
        }

        /// <summary>重建玩家列表行（NetList 任意变化时回调）。行视觉由 _playerRowPrefab 决定。</summary>
        private void RebuildPlayerList()
        {
            if (_listContent == null || _playerRowPrefab == null) return;

            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);

            RoomComponent room = _boundRoom;
            if (room == null || room.Players.Count == 0)
            {
                CreateRow("（暂无玩家）", new Color(1f, 1f, 1f, 0.4f));
                return;
            }

            for (int i = 0; i < room.Players.Count; i++)
            {
                RoomPlayerInfo info = room.Players[i];
                bool isLocal = info.ClientId == Net.LocalClientId;
                string line = $"{info.PlayerName}  #{info.ClientId}" + (isLocal ? "（我）" : string.Empty);
                CreateRow(line, isLocal ? LocalPlayerColor : Color.white);
            }
        }

        /// <summary>实例化一行玩家条目，支持 legacy Text 或 TMP_Text。</summary>
        private void CreateRow(string content, Color color)
        {
            GameObject row = Instantiate(_playerRowPrefab, _listContent);
            row.SetActive(true);

            Text text = row.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                text.text = content;
                text.color = color;
                return;
            }

            TMPro.TMP_Text tmp = row.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (tmp != null)
            {
                tmp.text = content;
                tmp.color = color;
                return;
            }

            Debug.LogWarning("[LobbyPanel] _playerRowPrefab 上找不到 Text / TMP_Text 组件。", this);
        }

        // ---------------- 工具 ----------------

        private void WireButton(Button button, UnityEngine.Events.UnityAction action, string fieldName)
        {
            if (button == null)
            {
                Debug.LogWarning($"[LobbyPanel] 未在 Inspector 赋值 {fieldName}。", this);
                return;
            }
            button.onClick.AddListener(action);
        }

        private static void UnwireButton(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null) button.onClick.RemoveListener(action);
        }

        private static void SetInteractable(Button button, bool value)
        {
            if (button != null) button.interactable = value;
        }
    }
}
