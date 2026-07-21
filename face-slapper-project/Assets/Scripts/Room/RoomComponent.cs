using System.Collections.Generic;
using FaceSlapper.Battle;
using FaceSlapper.Core;
using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Room
{
    /// <summary>
    /// 房间组件（场景网络对象，服务器权威）：
    /// 维护房间玩家列表（NetList）、房间状态（NetVar），
    /// 处理开始游戏、生成玩家、生成武器等服务器逻辑。
    /// 先实现局域网联机，后续再扩展互联网联机。
    /// </summary>
    public class RoomComponent : NetBehaviour
    {
        [SerializeField] private NetObject _playerPrefab;
        [SerializeField] private NetObject _weaponPrefab;

        /// <summary>房间内玩家列表（服务器写，全员同步）。</summary>
        public readonly NetList<RoomPlayerInfo> Players = new NetList<RoomPlayerInfo>();

        private readonly NetVar<RoomState> _state = new NetVar<RoomState>(RoomState.Lobby);

        public RoomState State => _state.Value;

        /// <summary>服务器端：clientId -> 已生成的玩家对象。</summary>
        private readonly Dictionary<int, NetObject> _serverPlayers = new Dictionary<int, NetObject>();

        protected override void Awake()
        {
            base.Awake();
            _state.OnChange += (prev, next) =>
            {
                EventBus.Publish(new RoomStateChangedEvent { State = next });
                Debug.Log($"[Room] 房间状态: {prev} -> {next}");
            };
        }

        public override void OnNetSpawnServer()
        {
            Net.OnRemoteClientConnected += OnClientConnected;
            Net.OnRemoteClientDisconnected += OnClientDisconnected;
        }

        public override void OnNetDespawnServer()
        {
            Net.OnRemoteClientConnected -= OnClientConnected;
            Net.OnRemoteClientDisconnected -= OnClientDisconnected;
            _serverPlayers.Clear();
        }

        private void OnClientConnected(int clientId)
        {
            Players.Add(new RoomPlayerInfo
            {
                ClientId = clientId,
                PlayerName = $"Player{clientId}",
                TeamId = 0,
                IsReady = true,
            });
            Debug.Log($"[Room] 玩家加入: Client {clientId}（当前 {Players.Count} 人）");
        }

        private void OnClientDisconnected(int clientId)
        {
            for (int i = Players.Count - 1; i >= 0; i--)
            {
                if (Players[i].ClientId == clientId)
                    Players.RemoveAt(i);
            }

            if (_serverPlayers.TryGetValue(clientId, out NetObject nob))
            {
                _serverPlayers.Remove(clientId);
                if (nob != null) Net.Server.Despawn(nob);
            }
            Debug.Log($"[Room] 玩家离开: Client {clientId}（当前 {Players.Count} 人）");
        }

        /// <summary>请求开始游戏（任何端可发起，GM 调试用途）。</summary>
        public void RequestStartGame() => SendServerRpc(nameof(CmdStartGame));

        /// <summary>请求在指定位置生成武器。</summary>
        public void RequestSpawnWeapon(Vector3 position) => SendServerRpc(nameof(CmdSpawnWeapon), position);

        /// <summary>请求设置玩家队伍。</summary>
        public void RequestSetTeam(int clientId, int teamId) => SendServerRpc(nameof(CmdSetTeam), clientId, teamId);

        [NetRpc]
        private void CmdStartGame()
        {
            if (_playerPrefab == null)
            {
                Debug.LogError("[Room] 未配置玩家 Prefab（_playerPrefab）。");
                return;
            }

            int index = 0;
            foreach (int clientId in Net.Server.ClientIds)
            {
                // 已有角色的连接跳过（允许中途调用为后进玩家补生成）。
                if (_serverPlayers.ContainsKey(clientId)) { index++; continue; }

                Vector3 pos = GetSpawnPosition(index);
                NetObject nob = Net.Server.Spawn(_playerPrefab, pos, Quaternion.identity, clientId);
                if (nob == null) { index++; continue; }

                var identity = nob.GetComponent<NetworkIdentity>();
                if (identity != null)
                {
                    identity.PlayerId.Value = clientId;
                    identity.ColorIndex.Value = index;
                    identity.TeamId.Value = GetTeamOf(clientId);
                    identity.PlayerName.Value = $"Player{clientId}";
                }

                _serverPlayers[clientId] = nob;
                index++;
            }

            _state.Value = RoomState.Playing;
            Debug.Log($"[Room] 游戏开始，已生成 {_serverPlayers.Count} 名玩家");
        }

        [NetRpc]
        private void CmdSpawnWeapon(Vector3 position)
        {
            if (_weaponPrefab == null)
            {
                Debug.LogError("[Room] 未配置武器 Prefab（_weaponPrefab）。");
                return;
            }
            Net.Server.Spawn(_weaponPrefab, position, Quaternion.identity);
        }

        [NetRpc]
        private void CmdSetTeam(int clientId, int teamId)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].ClientId == clientId)
                {
                    RoomPlayerInfo info = Players[i];
                    info.TeamId = teamId;
                    Players[i] = info;
                    return;
                }
            }
        }

        private int GetTeamOf(int clientId)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].ClientId == clientId)
                    return Players[i].TeamId;
            }
            return 0;
        }

        private static Vector3 GetSpawnPosition(int index)
        {
            List<Transform> points = PlayerSpawnPoints.Points;
            if (points.Count > 0)
            {
                Transform t = points[index % points.Count];
                return t.position;
            }
            // 没有出生点时的兜底：环形分布。
            float angle = index * 90f * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * 4f, 0.05f, Mathf.Sin(angle) * 4f);
        }
    }

    /// <summary>房间状态变化事件。</summary>
    public struct RoomStateChangedEvent
    {
        public RoomState State;
    }
}
