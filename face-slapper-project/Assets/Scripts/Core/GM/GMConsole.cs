using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FaceSlapper.Core
{
    /// <summary>
    /// GM 调试命令行（IMGUI 覆盖层）。按 ` 键呼出/关闭。
    /// 命令格式: /gm func MethodName arg1 arg2 ... 或 /gm func MethodName(arg1,arg2)
    /// 通过反射调用 GMComponent 内的同名 public 方法。
    /// </summary>
    public class GMConsole : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        private const int MaxLines = 100;
        private static readonly List<string> _lines = new List<string>(MaxLines);

        private string _input = string.Empty;
        private Vector2 _scroll;
        private bool _focusRequested;

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.BackQuote))
            {
                IsOpen = !IsOpen;
                if (IsOpen) _focusRequested = true;
            }
        }

        private void OnGUI()
        {
            if (!IsOpen) return;

            const float width = 680f;
            const float height = 320f;
            GUILayout.BeginArea(new Rect(10, Screen.height - height - 10, width, height), GUI.skin.box);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(height - 70));
            for (int i = 0; i < _lines.Count; i++)
                GUILayout.Label(_lines[i]);
            GUILayout.EndScrollView();

            GUI.SetNextControlName("GMInput");
            _input = GUILayout.TextField(_input, GUILayout.Height(24));
            if (_focusRequested)
            {
                GUI.FocusControl("GMInput");
                _focusRequested = false;
            }

            if (Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                Execute();
            }

            GUILayout.Label("格式: /gm func MethodName arg1 arg2（` 关闭，回车执行）");
            GUILayout.EndArea();
        }

        /// <summary>向命令行窗口输出一行。</summary>
        public static void Log(string line)
        {
            if (_lines.Count >= MaxLines) _lines.RemoveAt(0);
            _lines.Add(line);
        }

        public void Execute()
        {
            string cmd = _input;
            _input = string.Empty;
            _focusRequested = true;
            Execute(cmd);
            Event.current.Use();
        }

        public void Execute(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            Log("> " + raw);

            string s = raw.Trim();
            const string prefix = "/gm func";
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                s = s.Substring(prefix.Length).Trim();

            string methodName;
            string[] args;
            Match parens = Regex.Match(s, @"^(\w+)\s*\((.*)\)\s*$");
            if (parens.Success)
            {
                methodName = parens.Groups[1].Value;
                string inner = parens.Groups[2].Value.Trim();
                args = inner.Length == 0
                    ? Array.Empty<string>()
                    : inner.Split(',').Select(a => a.Trim()).ToArray();
            }
            else
            {
                string[] parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return;
                methodName = parts[0];
                args = parts.Skip(1).ToArray();
            }

            GMComponent gm = GameManager.HasInstance ? GameManager.Instance.Get<GMComponent>() : null;
            if (gm == null)
            {
                Log("错误: GMComponent 未注册");
                return;
            }

            List<MethodInfo> candidates = gm.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(mi => mi.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidates.Count == 0)
            {
                Log($"未找到方法 '{methodName}'。可用命令:");
                foreach (MethodInfo mi in gm.GetType()
                             .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    Log("  " + mi.Name + "(" + string.Join(", ",
                        mi.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}")) + ")");
                }
                return;
            }

            foreach (MethodInfo mi in candidates)
            {
                ParameterInfo[] ps = mi.GetParameters();
                if (ps.Length != args.Length) continue;
                if (!TryConvertArgs(args, ps, out object[] converted)) continue;

                try
                {
                    object result = mi.Invoke(gm, converted);
                    Log($"{mi.Name} 执行成功" + (result != null ? $": {result}" : string.Empty));
                }
                catch (Exception e)
                {
                    Exception inner = e.InnerException ?? e;
                    Log($"执行异常: {inner.Message}");
                }
                return;
            }

            Log($"参数个数/类型不匹配: {methodName} 需要 " +
                string.Join(" 或 ", candidates.Select(mi =>
                    "(" + string.Join(",", mi.GetParameters().Select(p => p.ParameterType.Name)) + ")")));
        }

        private static bool TryConvertArgs(string[] args, ParameterInfo[] ps, out object[] converted)
        {
            converted = new object[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                if (!TryConvert(args[i], ps[i].ParameterType, out converted[i]))
                    return false;
            }
            return true;
        }

        private static bool TryConvert(string raw, Type target, out object value)
        {
            value = null;
            try
            {
                if (target == typeof(string)) value = raw;
                else if (target == typeof(int)) value = int.Parse(raw);
                else if (target == typeof(float)) value = float.Parse(raw);
                else if (target == typeof(double)) value = double.Parse(raw);
                else if (target == typeof(bool)) value = raw == "1" || bool.Parse(raw);
                else if (target.IsEnum) value = Enum.Parse(target, raw, true);
                else return false;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
