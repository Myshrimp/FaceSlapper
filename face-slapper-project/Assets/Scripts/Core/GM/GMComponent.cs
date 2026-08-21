using FaceSlapper.Match;
using FaceSlapper.Room;
using UnityEngine;

namespace FaceSlapper.Core
{
    /// <summary>
    /// GM 功能库：命令行 /gm func MethodName(...) 通过反射调用本组件内的同名 public 方法。
    /// 新增 GM 功能时直接在这里加 public 方法即可（参数支持 int/float/bool/string）。
    /// </summary>
    public class GMComponent : MonoBehaviour, IGameComponent
    {
        private RoomHandler Room => GameManager.Instance.Get<RoomHandler>();

        private MatchHandler Match => GameManager.Instance.Get<MatchHandler>();

        public void OnInit() { }

        public void OnShutdown() { }

        /// <summary>开启局域网主机（同时作为服务器与客户端）。</summary>
        public string Host()
        {
            bool ok = Room.Host();
            return ok ? "主机已启动 (Host = Server + Client)" : "启动失败，请检查场景中 NetworkManager";
        }

        /// <summary>加入局域网房间。用法: Join 192.168.1.100</summary>
        public string Join(string ip)
        {
            bool ok = Room.Join(ip);
            return ok ? $"正在连接 {ip} ..." : "连接发起失败";
        }

        public string Join()
        {
            return Join("127.0.0.1");
        }

        /// <summary>停止服务器/客户端。</summary>
        public string Stop()
        {
            Room.Stop();
            return "已停止网络连接";
        }

        /// <summary>开始游戏：服务器为所有已连接玩家生成角色（需要 Host 权限）。</summary>
        public string StartGame()
        {
            return Room.StartGame() ? "开始游戏指令已发送" : "失败：未连接网络或找不到 RoomComponent";
        }

        /// <summary>在指定位置生成一把武器。用法: SpawnWeapon 2 3</summary>
        public string SpawnWeapon(float x, float z)
        {
            return Room.SpawnWeapon(new Vector3(x, 1f, z)) ? $"已在 ({x}, {z}) 生成武器" : "失败：未连接网络";
        }

        /// <summary>打印房间内所有玩家。</summary>
        public string ListPlayers()
        {
            return Room.ListPlayers();
        }

        /// <summary>设置玩家队伍。用法: SetTeam 0 1</summary>
        public string SetTeam(int clientId, int teamId)
        {
            return Room.SetTeam(clientId, teamId) ? $"已请求设置玩家 {clientId} 为队伍 {teamId}" : "失败：未连接网络";
        }

        /// <summary>列出所有可选模式。用法: ListModes</summary>
        public string ListModes()
        {
            return Match.ListModes();
        }

        /// <summary>选择对局模式。用法: SelectMode score_race</summary>
        public string SelectMode(string modeId)
        {
            return Match.SelectMode(modeId) ? $"已请求选择模式 {modeId}" : "失败：未连接网络";
        }

        /// <summary>注册本机玩家到对局（需已选择模式）。用法: RegisterPlayer</summary>
        public string RegisterPlayer()
        {
            return Match.Register() ? "已请求注册本机玩家" : "失败：未连接网络";
        }

        /// <summary>注销本机玩家。用法: UnregisterPlayer</summary>
        public string UnregisterPlayer()
        {
            return Match.Unregister() ? "已请求注销本机玩家" : "失败：未连接网络";
        }

        /// <summary>开始对局。用法: StartMatch</summary>
        public string StartMatch()
        {
            return Match.StartMatch() ? "开始指令已发送" : "失败：未连接网络或找不到 MatchComponent";
        }

        /// <summary>结束对局（按当前比分结算）。用法: EndMatch</summary>
        public string EndMatch()
        {
            return Match.EndMatch() ? "结束指令已发送" : "失败：未连接网络";
        }

        /// <summary>打印当前对局信息。用法: MatchInfo</summary>
        public string MatchInfo()
        {
            return Match.MatchInfo();
        }

        /// <summary>开始帧同步会话（lockstep，为所有已连接玩家生成 PlayerFrameSync）。用法: StartFrameSync</summary>
        public string StartFrameSync()
        {
            var mgr = Object.FindObjectOfType<FrameSync.FrameSyncManager>();
            if (mgr == null) return "失败：场景中找不到 FrameSyncManager（请先运行菜单 FaceSlapper/Setup FrameSync）";
            mgr.RequestStartFrameSync();
            return "帧同步会话开始指令已发送";
        }

        /// <summary>结束帧同步会话并销毁帧同步玩家。用法: StopFrameSync</summary>
        public string StopFrameSync()
        {
            var mgr = Object.FindObjectOfType<FrameSync.FrameSyncManager>();
            if (mgr == null) return "失败：场景中找不到 FrameSyncManager（请先运行菜单 FaceSlapper/Setup FrameSync）";
            mgr.RequestStopFrameSync();
            return "帧同步会话结束指令已发送";
        }

        /// <summary>运行帧同步确定性自检（1800 tick 双实例哈希比对）。用法: FrameSyncTest</summary>
        public string FrameSyncTest()
        {
            bool ok = FrameSync.FrameSyncSelfTest.RunDeterminismCheck();
            return ok ? "帧同步确定性自检通过（详见 Console）" : "帧同步确定性自检失败（详见 Console 错误日志）";
        }

        /// <summary>帧同步诊断：打印会话快照，并开启 10 秒诊断窗口（每秒输出水位滞后/回滚次数/最大深度/预测占比）。用法: FrameSyncDebug</summary>
        public string FrameSyncDebug()
        {
            var mgr = Object.FindObjectOfType<FrameSync.FrameSyncManager>();
            if (mgr == null) return "失败：场景中找不到 FrameSyncManager";
            return mgr.DumpDebugState();
        }

        /// <summary>模拟网络延迟（本端帧同步输入消息收发均延迟，用于回滚实测；仅本端生效，双端需各自设置）。用法: FrameSyncNetDelay 100（固定 100ms）或 FrameSyncNetDelay 100 20（±20ms 抖动），FrameSyncNetDelay 0 关闭</summary>
        public string FrameSyncNetDelay(int ms)
        {
            return FrameSyncNetDelay(ms, 0);
        }

        public string FrameSyncNetDelay(int ms, int jitterMs)
        {
            var mgr = Object.FindObjectOfType<FrameSync.FrameSyncManager>();
            if (mgr == null) return "失败：场景中找不到 FrameSyncManager";
            mgr.SetDebugNetDelay(ms, jitterMs);
            return ms <= 0 && jitterMs <= 0
                ? "已关闭模拟网络延迟"
                : $"已开启模拟网络延迟：{Mathf.Max(0, ms)}ms ± {Mathf.Max(0, jitterMs)}ms（仅本端收发帧同步消息）";
        }

        /// <summary>写一条日志。用法: Log hello</summary>
        public string Log(string msg)
        {
            Debug.Log($"[GM] {msg}");
            return $"已记录: {msg}";
        }
    }
}
