using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace FaceSlapper.Networking
{
    /// <summary>
    /// 标记一个方法可被远程调用（经 SendServerRpc/SendObserversRpc/SendTargetRpc 按名字派发）。
    /// 参数仅支持 NetSerializer 可序列化的类型（int/long/float/bool/string/Vector2/Vector3/Quaternion/枚举）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public class NetRpcAttribute : Attribute { }

    /// <summary>按类型缓存 [NetRpc] 方法表。</summary>
    internal static class NetRpcDispatcher
    {
        private static readonly Dictionary<Type, Dictionary<string, MethodInfo>> Cache =
            new Dictionary<Type, Dictionary<string, MethodInfo>>(32);

        public static Dictionary<string, MethodInfo> GetRpcMethods(Type type)
        {
            if (Cache.TryGetValue(type, out Dictionary<string, MethodInfo> map))
                return map;

            map = new Dictionary<string, MethodInfo>(8);
            // 沿继承链逐层扫描：Type.GetMethods 不会返回基类的 private 方法，
            // 而 [NetRpc] 方法常常声明在基类（如 WeaponBase.CmdPickup）。
            // 派生类优先（先扫派生层），同名方法以派生类为准。
            for (Type t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(object); t = t.BaseType)
            {
                MethodInfo[] methods = t.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                foreach (MethodInfo mi in methods)
                {
                    if (mi.GetCustomAttribute<NetRpcAttribute>() != null && !map.ContainsKey(mi.Name))
                        map[mi.Name] = mi;
                }
            }
            Cache[type] = map;
            return map;
        }

        /// <summary>把反序列化后的参数适配到方法签名并调用。</summary>
        public static void Invoke(MethodInfo method, object target, object[] args)
        {
            ParameterInfo[] ps = method.GetParameters();
            if (ps.Length != args.Length)
            {
                UnityEngine.Debug.LogWarning($"[NetRpc] {method.Name} 参数个数不匹配: 需要 {ps.Length}, 实际 {args.Length}");
                return;
            }

            for (int i = 0; i < ps.Length; i++)
            {
                if (args[i] == null || ps[i].ParameterType.IsInstanceOfType(args[i])) continue;
                // 枚举以 int 传输。
                if (ps[i].ParameterType.IsEnum && args[i] is int intVal)
                    args[i] = Enum.ToObject(ps[i].ParameterType, intVal);
                else
                    args[i] = Convert.ChangeType(args[i], ps[i].ParameterType);
            }

            method.Invoke(target, args);
        }
    }
}
