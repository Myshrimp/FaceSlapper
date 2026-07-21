using System;
using System.Collections;
using System.Collections.Generic;

namespace FaceSlapper.Networking
{
    /// <summary>
    /// 服务器权威同步变量：服务器写入后自动同步到所有客户端（可靠通道）。
    /// 只能声明为 NetBehaviour 的字段，由所属 NetObject 自动注册。
    /// 客户端只读（写入会被忽略并警告）。
    /// </summary>
    public class NetVar<T> : INetVarEntry, IInternalNetVar
    {
        private T _value;

        /// <summary>值变化回调（prev, next）。服务器与客户端都会触发。</summary>
        public event Action<T, T> OnChange;

        /// <summary>所属 NetObject 内的注册 Id（由框架赋值）。</summary>
        internal int RegisteredId = -1;

        internal NetBehaviour Host;

        void IInternalNetVar.SetRegistration(int id, NetBehaviour host)
        {
            RegisteredId = id;
            Host = host;
        }

        public NetVar(T initialValue = default)
        {
            _value = initialValue;
        }

        public T Value
        {
            get => _value;
            set
            {
                if (Host == null)
                {
                    // 尚未注册（Awake 前赋值），直接设置，同步在生成后按全量状态覆盖。
                    _value = value;
                    return;
                }
                Host.WriteNetVar(this, value);
            }
        }

        /// <summary>本地应用新值（框架内部使用）。返回是否有实际变化。</summary>
        internal bool ApplyLocal(T next, bool forceNotify = false)
        {
            bool changed = !EqualityComparer<T>.Default.Equals(_value, next);
            T prev = _value;
            _value = next;
            if (changed || forceNotify)
                OnChange?.Invoke(prev, next);
            return changed;
        }

        byte[] INetVarEntry.Serialize() => NetSerializer.WriteValue(_value);

        void INetVarEntry.DeserializeAndApply(byte[] payload)
        {
            T next = NetSerializer.ReadValue<T>(payload);
            ApplyLocal(next);
        }
    }

    /// <summary>
    /// 服务器权威同步列表：服务器增删改后整体同步到所有客户端（元素少时开销可忽略）。
    /// </summary>
    public class NetList<T> : INetVarEntry, IInternalNetVar, IEnumerable<T> where T : INetSerializable, new()
    {
        private readonly List<T> _items = new List<T>(16);

        /// <summary>任意变化时回调。</summary>
        public event Action OnChange;

        internal int RegisteredId = -1;
        internal NetBehaviour Host;

        void IInternalNetVar.SetRegistration(int id, NetBehaviour host)
        {
            RegisteredId = id;
            Host = host;
        }

        public int Count => _items.Count;

        public T this[int index]
        {
            get => _items[index];
            set
            {
                _items[index] = value;
                MarkDirty();
            }
        }

        public void Add(T item)
        {
            _items.Add(item);
            MarkDirty();
        }

        public void RemoveAt(int index)
        {
            _items.RemoveAt(index);
            MarkDirty();
        }

        public void Clear()
        {
            _items.Clear();
            MarkDirty();
        }

        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private void MarkDirty()
        {
            OnChange?.Invoke();
            Host?.MarkNetVarDirty(RegisteredId);
        }

        byte[] INetVarEntry.Serialize()
        {
            using var ms = new System.IO.MemoryStream();
            using var w = new System.IO.BinaryWriter(ms);
            w.Write(_items.Count);
            foreach (T item in _items) item.Write(w);
            w.Flush();
            return ms.ToArray();
        }

        void INetVarEntry.DeserializeAndApply(byte[] payload)
        {
            using var ms = new System.IO.MemoryStream(payload);
            using var r = new System.IO.BinaryReader(ms);
            int count = r.ReadInt32();
            _items.Clear();
            for (int i = 0; i < count; i++)
            {
                var item = new T();
                item.Read(r);
                _items.Add(item);
            }
            OnChange?.Invoke();
        }
    }
}
