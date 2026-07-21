using System.Collections.Generic;
using UnityEngine;

namespace FaceSlapper.Networking
{
    /// <summary>
    /// 后端无关的位置/旋转同步组件（替代各网络库原生的 NetworkTransform）：
    /// 控制端（所有者，或无所有者时的服务器）按固定频率经不可靠通道广播；
    /// 接收端做延迟插值。接收端刚体应为运动学（由各自的控制器脚本保证）。
    /// </summary>
    [RequireComponent(typeof(NetObject))]
    public class NetTransformSync : MonoBehaviour
    {
        [Header("发送")]
        [SerializeField] private float _sendInterval = 0.05f;

        [Header("插值")]
        [SerializeField] private float _interpolationDelay = 0.1f;

        private struct Snapshot
        {
            public float Time;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        private NetObject _netObject;
        private float _sendTimer;
        private readonly List<Snapshot> _snapshots = new List<Snapshot>(32);

        private void Awake()
        {
            _netObject = GetComponent<NetObject>();
            _netObject.OnTransformReceived += OnTransform;
        }

        private void OnDestroy()
        {
            if (_netObject != null)
                _netObject.OnTransformReceived -= OnTransform;
        }

        private void OnTransform(Vector3 position, Quaternion rotation)
        {
            _snapshots.Add(new Snapshot { Time = Time.time, Position = position, Rotation = rotation });

            // 只保留最近 2 秒。
            float cutoff = Time.time - 2f;
            int stale = 0;
            while (stale < _snapshots.Count && _snapshots[stale].Time < cutoff) stale++;
            if (stale > 0) _snapshots.RemoveRange(0, stale);
        }

        private void Update()
        {
            if (!_netObject.IsSpawned) return;

            if (_netObject.IsController)
            {
                _sendTimer += Time.deltaTime;
                if (_sendTimer >= _sendInterval)
                {
                    _sendTimer = 0f;
                    _netObject.Bridge?.SendTransform(transform.position, transform.rotation);
                }
                return;
            }

            Interpolate();
        }

        private void Interpolate()
        {
            int count = _snapshots.Count;
            if (count == 0) return;

            float renderTime = Time.time - _interpolationDelay;

            // 找夹住 renderTime 的两个快照。
            int newest = count - 1;
            if (renderTime >= _snapshots[newest].Time)
            {
                ApplySnapshot(_snapshots[newest]);
                return;
            }
            if (renderTime <= _snapshots[0].Time)
            {
                ApplySnapshot(_snapshots[0]);
                return;
            }

            for (int i = newest; i > 0; i--)
            {
                Snapshot newer = _snapshots[i];
                Snapshot older = _snapshots[i - 1];
                if (older.Time > renderTime) continue;

                float span = newer.Time - older.Time;
                float t = span > 0.0001f ? (renderTime - older.Time) / span : 1f;
                Apply(
                    Vector3.LerpUnclamped(older.Position, newer.Position, Mathf.Clamp01(t)),
                    Quaternion.SlerpUnclamped(older.Rotation, newer.Rotation, Mathf.Clamp01(t)));
                return;
            }

            ApplySnapshot(_snapshots[newest]);
        }

        private void ApplySnapshot(Snapshot s) => Apply(s.Position, s.Rotation);

        private void Apply(Vector3 pos, Quaternion rot)
        {
            // 偏差过大时同样直接设置（重生/传送场景由位置跳变自然处理）。
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
