using System.Collections.Generic;
using FaceSlapper.Battle;
using FaceSlapper.Core;
using UnityEditor;
using UnityEngine;

namespace FaceSlapper.EditorTools
{
    /// <summary>
    /// FSM 调试面板：Play 模式下实时列出场景中所有 StateMachineComponent 的
    /// 激活分支路径（如 Player(Clone)/Normal/Moving、BoxingGlove(Clone)/Attack），
    /// 按根层状态着色，并显示当前分支已持续的秒数（核对眩晕/攻击时长是否符合预期）。
    /// 点击条目可定位选中对应对象。
    /// 菜单: FaceSlapper/FSM 调试面板
    /// </summary>
    public class FsmDebugWindow : EditorWindow
    {
        /// <summary>分支停留计时：路径变化时重置起点。</summary>
        private class DwellRecord
        {
            public string Path;
            public double Since;
        }

        private readonly Dictionary<StateMachineComponent, DwellRecord> _dwell =
            new Dictionary<StateMachineComponent, DwellRecord>();

        private Vector2 _scroll;
        private bool _onlyPlayers;
        private GUIStyle _pathStyle;

        [MenuItem("FaceSlapper/FSM 调试面板", priority = 20)]
        public static void Open()
        {
            GetWindow<FsmDebugWindow>("FSM 调试").Show();
        }

        private void OnInspectorUpdate()
        {
            // Play 模式下约 10Hz 重绘，足够观察状态流转。
            if (EditorApplication.isPlaying) Repaint();
        }

        private void OnGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("进入 Play 模式后实时显示所有状态机的激活分支。", MessageType.Info);
                return;
            }

            if (_pathStyle == null)
                _pathStyle = new GUIStyle(EditorStyles.label) { richText = true };

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _onlyPlayers = GUILayout.Toggle(_onlyPlayers, "只看玩家", EditorStyles.toolbarButton, GUILayout.Width(70));
            GUILayout.FlexibleSpace();
            GUILayout.Label("约 10Hz 刷新", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            // includeInactive：TickEnabled 关掉的实体也能看到（便于排查"为什么不走状态"）。
            var comps = Object.FindObjectsOfType<StateMachineComponent>(true);
            System.Array.Sort(comps, (a, b) => string.CompareOrdinal(a.name, b.name));

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            int shown = 0;
            foreach (StateMachineComponent comp in comps)
            {
                if (_onlyPlayers && comp.GetComponent<PlayerFsmComponent>() == null) continue;
                DrawEntry(comp);
                shown++;
            }
            if (shown == 0)
                GUILayout.Label(_onlyPlayers ? "场景中没有玩家状态机。" : "场景中没有注册任何状态机。", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndScrollView();
        }

        private void DrawEntry(StateMachineComponent comp)
        {
            string rootState = comp.Root != null && comp.Root.CurrentState != null
                ? comp.Root.CurrentState.Name : "-";
            bool ticking = comp.TickEnabled && comp.isActiveAndEnabled;
            string branchPath = comp.GetBranchPath();

            EditorGUILayout.BeginHorizontal();

            // 对象名：点击定位并选中。
            if (GUILayout.Button(comp.name, EditorStyles.linkLabel, GUILayout.Width(160)))
            {
                Selection.activeGameObject = comp.gameObject;
                EditorGUIUtility.PingObject(comp.gameObject);
            }

            // 分支路径：按根层状态着色（Normal 绿 / Launched 橙 / Stunned 红 / 其它灰）。
            string path = ticking ? branchPath : branchPath + "  <i>(已停表)</i>";
            GUILayout.Label($"<color={ColorOf(rootState)}>{path}</color>", _pathStyle);

            GUILayout.FlexibleSpace();

            // 停留时长：分支路径（含子状态）变化时重新计时。
            GUILayout.Label(FormatDwell(comp, branchPath), EditorStyles.miniLabel, GUILayout.Width(52));
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>当前分支已持续的秒数（窗口按约 10Hz 采样，精度足够核对计时）。</summary>
        private string FormatDwell(StateMachineComponent comp, string branchPath)
        {
            double now = EditorApplication.timeSinceStartup;
            if (!_dwell.TryGetValue(comp, out DwellRecord record) || record.Path != branchPath)
            {
                record = new DwellRecord { Path = branchPath, Since = now };
                _dwell[comp] = record;
            }
            return (now - record.Since).ToString("0.0") + "s";
        }

        /// <summary>退出 Play 模式时清掉已销毁实体的计时记录。</summary>
        private void OnDisable()
        {
            _dwell.Clear();
        }

        private static string ColorOf(string rootState)
        {
            switch (rootState)
            {
                case PlayerNormalState.StateName: return "#7FD67F";
                case PlayerLaunchedState.StateName: return "#F0A040";
                case PlayerStunnedState.StateName: return "#F06060";
                case PlayerDashState.StateName: return "#5FD3D0";
                case "Attack": return "#E8D050";
                case "Idle": return "#9FC5E8";
                default: return "#AAAAAA";
            }
        }
    }
}
