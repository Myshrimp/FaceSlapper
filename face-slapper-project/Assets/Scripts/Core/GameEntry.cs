using FaceSlapper.Battle;
using FaceSlapper.Input;
using FaceSlapper.Match;
using FaceSlapper.Network;
using FaceSlapper.Room;
using FaceSlapper.UI;
using UnityEngine;

namespace FaceSlapper.Core
{
    /// <summary>
    /// 游戏引导器：场景加载时创建 GameManager 并按顺序注册所有游戏组件。
    /// 场景中只需放一个挂了 GameEntry 的物体即可。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class GameEntry : MonoBehaviour
    {
        [Tooltip("游戏启动时自动打开的 UI 面板预制体（如大厅面板），可留空。")]
        [SerializeField] private UIPanel[] _startupPanels = new UIPanel[0];

        private void Awake()
        {
            GameManager gm = GameManager.Instance;

            // 基础组件优先，业务组件在后。
            gm.AddAndRegister<LogComponent>();
            gm.AddAndRegister<PoolComponent>();
            gm.AddAndRegister<StateMachineManager>();
            gm.AddAndRegister<InputComponent>();
            gm.AddAndRegister<UIComponent>();
            gm.AddAndRegister<NetworkComponent>();
            gm.AddAndRegister<TickComponent>();
            gm.AddAndRegister<SceneManagementComponent>();
            gm.AddAndRegister<RoomHandler>();
            gm.AddAndRegister<MatchHandler>();
            gm.AddAndRegister<GMComponent>();
            gm.AddAndRegister<HitFeedbackComponent>();

            // GM 调试命令行（IMGUI，不属于注册组件）。
            if (gm.GetComponent<GMConsole>() == null)
                gm.gameObject.AddComponent<GMConsole>();

            // 启动面板：在 Inspector 中配置面板预制体（如大厅），启动时经 UIManager 打开。
            UIComponent ui = gm.Get<UIComponent>();
            for (int i = 0; i < _startupPanels.Length; i++)
            {
                if (_startupPanels[i] != null) ui.Manager.Open(_startupPanels[i]);
            }
        }
    }
}
