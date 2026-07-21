using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FaceSlapper.Networking
{
    /// <summary>可由 NetSerializer 直接序列化的自定义类型。</summary>
    public interface INetSerializable
    {
        void Write(BinaryWriter writer);
        void Read(BinaryReader reader);
    }

    /// <summary>
    /// 网络序列化工具：支持 int/long/float/bool/string/Vector2/Vector3/Quaternion/枚举/
    /// INetSerializable 及其 List。RPC 参数使用类型标签编码。
    /// </summary>
    public static class NetSerializer
    {
        // ---------------- 值序列化（无类型标签，按声明类型解码） ----------------

        public static byte[] WriteValue<T>(T value)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            WriteValue(w, value, typeof(T));
            w.Flush();
            return ms.ToArray();
        }

        public static T ReadValue<T>(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms);
            return (T)ReadValue(r, typeof(T));
        }

        public static void WriteValue(BinaryWriter w, object value, Type type)
        {
            if (type == typeof(int)) w.Write((int)value);
            else if (type == typeof(long)) w.Write((long)value);
            else if (type == typeof(float)) w.Write((float)value);
            else if (type == typeof(bool)) w.Write((bool)value);
            else if (type == typeof(string)) w.Write((string)value ?? string.Empty);
            else if (type == typeof(Vector2)) { Vector2 v = (Vector2)value; w.Write(v.x); w.Write(v.y); }
            else if (type == typeof(Vector3)) { Vector3 v = (Vector3)value; w.Write(v.x); w.Write(v.y); w.Write(v.z); }
            else if (type == typeof(Quaternion)) { Quaternion q = (Quaternion)value; w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
            else if (type.IsEnum) w.Write(Convert.ToInt32(value));
            else if (typeof(INetSerializable).IsAssignableFrom(type)) ((INetSerializable)value).Write(w);
            else throw new NotSupportedException($"[NetSerializer] 不支持的类型: {type}");
        }

        public static object ReadValue(BinaryReader r, Type type)
        {
            if (type == typeof(int)) return r.ReadInt32();
            if (type == typeof(long)) return r.ReadInt64();
            if (type == typeof(float)) return r.ReadSingle();
            if (type == typeof(bool)) return r.ReadBoolean();
            if (type == typeof(string)) return r.ReadString();
            if (type == typeof(Vector2)) return new Vector2(r.ReadSingle(), r.ReadSingle());
            if (type == typeof(Vector3)) return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            if (type == typeof(Quaternion)) return new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            if (type.IsEnum) return Enum.ToObject(type, r.ReadInt32());
            if (typeof(INetSerializable).IsAssignableFrom(type))
            {
                var obj = (INetSerializable)Activator.CreateInstance(type);
                obj.Read(r);
                return obj;
            }
            throw new NotSupportedException($"[NetSerializer] 不支持的类型: {type}");
        }

        // ---------------- RPC 参数序列化（带类型标签） ----------------

        private const byte TagInt = 1;
        private const byte TagLong = 2;
        private const byte TagFloat = 3;
        private const byte TagBool = 4;
        private const byte TagString = 5;
        private const byte TagVector2 = 6;
        private const byte TagVector3 = 7;
        private const byte TagQuaternion = 8;

        public static byte[] WriteArgs(object[] args)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            int count = args?.Length ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
            {
                object a = args[i];
                switch (a)
                {
                    case int v: w.Write(TagInt); w.Write(v); break;
                    case long v: w.Write(TagLong); w.Write(v); break;
                    case float v: w.Write(TagFloat); w.Write(v); break;
                    case bool v: w.Write(TagBool); w.Write(v); break;
                    case string v: w.Write(TagString); w.Write(v ?? string.Empty); break;
                    case Vector2 v: w.Write(TagVector2); w.Write(v.x); w.Write(v.y); break;
                    case Vector3 v: w.Write(TagVector3); w.Write(v.x); w.Write(v.y); w.Write(v.z); break;
                    case Quaternion v: w.Write(TagQuaternion); w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w); break;
                    case Enum e: w.Write(TagInt); w.Write(Convert.ToInt32(e)); break;
                    default: throw new NotSupportedException($"[NetSerializer] RPC 参数不支持的类型: {a?.GetType()}");
                }
            }
            w.Flush();
            return ms.ToArray();
        }

        public static object[] ReadArgs(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms);
            int count = r.ReadInt32();
            var args = new object[count];
            for (int i = 0; i < count; i++)
            {
                byte tag = r.ReadByte();
                args[i] = tag switch
                {
                    TagInt => r.ReadInt32(),
                    TagLong => r.ReadInt64(),
                    TagFloat => r.ReadSingle(),
                    TagBool => r.ReadBoolean(),
                    TagString => r.ReadString(),
                    TagVector2 => new Vector2(r.ReadSingle(), r.ReadSingle()),
                    TagVector3 => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                    TagQuaternion => new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                    _ => throw new NotSupportedException($"[NetSerializer] 未知参数标签: {tag}"),
                };
            }
            return args;
        }
    }
}
