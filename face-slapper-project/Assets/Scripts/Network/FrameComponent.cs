using FaceSlapper.Core;
using FaceSlapper.Input;
using FaceSlapper.Networking;
using System;
using System.Collections.Generic;
using UnityEngine;
namespace FaceSlapper.TestFrameSync
{
    /// <summary>
    /// 乐观帧同步组件：Owner 本地预测并上报输入，其他端沿用上一次权威输入做预测；
    /// 收到服务器回传的权威帧后若与预测不一致，则回滚并立即重模拟到当前帧。
    /// 帧号单调递增，frames 为按帧号取模索引的环形缓冲，最多可回滚 FramesToKeep 帧。
    /// </summary>
    public class FrameComponent : NetBehaviour
    {
        public int FramesToKeep = 1024;
        public int AuthFrame = 0;
        public int CurrentFrame = 0;
        public int ServerFrame = 0;

        [Serializable]
        public struct AnalysisData
        {
            public int RevertCount;
            public int RollbackCount;
            public int Latency;
        }

        public AnalysisData stat;


        private readonly Dictionary<string, Action<InputFrame>> inputFrameFuncMap = new();
        private readonly Dictionary<string, Action<InputFrame>> inputFrameRevertFuncMap = new();
        private InputFrame[] frames; //本地维护的环形缓冲

        private int _pendingJump;

        #region Replay
        public int snapshotSize = 1024 * 4;
        public struct GameSnapShot
        {
            public InputFrame[] frames;
        }

        public GameSnapShot[] snapshots;
        public void Replay()
        {
            for(int i=0;i<snapshots.Length;i++)
            {
                for(int j=0;j<snapshots[i].frames.Length;j++)
                {
                    Simulate(snapshots[i].frames[j]);
                }
            }
        }
        #endregion

        protected override void Awake()
        {
            base.Awake();
            frames = new InputFrame[FramesToKeep];
        }

        private void Update()
        {
            //两次 FixedUpdate 之间的 GetKeyDown 会丢失，在 Update 中锁存到下一固定帧消费
            if (IsOwner && UnityEngine.Input.GetKeyDown(KeyCode.Space))
                _pendingJump = 1;
        }

        private void FixedUpdate()
        {
            //非 Owner 假设玩家的输出与上次权威的一样
            InputFrame frame = IsOwner ? ComposeInputFrame() : frames[FrameIndex(AuthFrame)];
            Simulate(frame);
            if (IsOwner)
                SendFrame(frame);
            CurrentFrame += 1;
            if (IsServer)
            {
                ServerFrame = CurrentFrame;
            }
        }

        private int FrameIndex(int frame) => frame % FramesToKeep;

        private void Simulate(InputFrame frame)
        {
            frames[FrameIndex(CurrentFrame)] = frame;
            ApplyInputFrame(frame);
        }

        private void ApplyInputFrame(InputFrame frame)
        {
            foreach (var func in inputFrameFuncMap.Values)
            {
                func(frame);
            }
        }

        private void Revert(InputFrame frame)
        {
            foreach (var func in inputFrameRevertFuncMap.Values)
            {
                func(frame);
            }
            stat.RevertCount += 1;
        }

        private InputFrame ComposeInputFrame()
        {
            InputFrame frame = new InputFrame();

            frame.MoveX = (int)UnityEngine.Input.GetAxisRaw("Horizontal");
            frame.MoveZ = (int)UnityEngine.Input.GetAxisRaw("Vertical");
            frame.Jump = _pendingJump;
            _pendingJump = 0;

            return frame;
        }

        private void SendFrame(InputFrame frame)
        {
            SendServerRpc(nameof(SyncFrame), frame.MoveX, frame.MoveY, frame.MoveZ, frame.Jump);
        }

        private bool NeedRevert(InputFrame comingFrame)
        {
            InputFrame predictedFrame = frames[FrameIndex(comingFrame.AuthFrame)];
            return CompareTwoFrames(comingFrame, predictedFrame);
        }

        /// <summary>撤销 [authFrame, CurrentFrame) 内已模拟的预测帧。</summary>
        private void Rollback(int authFrame)
        {
            for (int i = CurrentFrame - 1; i >= authFrame; i--)
            {
                Revert(frames[FrameIndex(i)]);
            }
            stat.RollbackCount += 1;
        }

        /// <summary>用缓冲中的输入从 authFrame 立即重模拟到 CurrentFrame（输入不变，仅重放表现）。</summary>
        private void Resimulate(int authFrame)
        {
            for (int i = authFrame; i < CurrentFrame; i++)
            {
                ApplyInputFrame(frames[FrameIndex(i)]);
            }
        }

        private bool CompareTwoFrames(InputFrame auth, InputFrame predicted)
        {
            if (auth.MoveX != predicted.MoveX) return true;
            if (auth.MoveY != predicted.MoveY) return true;
            if (auth.MoveZ != predicted.MoveZ) return true;
            if (auth.Jump != predicted.Jump) return true;
            return false;
        }

        public void RegisterInputFrameFunc(string key, Action<InputFrame> func)
        {
            inputFrameFuncMap[key] = func;
        }

        public void RegisterInputFrameRevertFunc(string key, Action<InputFrame> func)
        {
            inputFrameRevertFuncMap[key] = func;
        }

        [NetRpc]
        public void OnReceiveInputFrame(int serverFrame, int moveX, int moveY, int moveZ, int jump)
        {
            //Owner 的预测就是自己的真实输入，与服务器回传必然一致，无需校验
            if (IsOwner) return;

            AuthFrame = serverFrame;
            InputFrame comingFrame = new InputFrame(serverFrame, moveX, moveY, moveZ, jump);
            if (!NeedRevert(comingFrame)) return;

            if (serverFrame < CurrentFrame - FramesToKeep)
            {
                Debug.LogWarning($"[FrameComponent] 权威帧 {serverFrame} 超出回滚缓冲（当前 {CurrentFrame}），无法完整回滚");
                return;
            }

            Rollback(serverFrame);
            frames[FrameIndex(serverFrame)] = comingFrame;
            Resimulate(serverFrame);
        }

        [NetRpc]
        public void SyncFrame(int moveX, int moveY, int moveZ, int jump)
        {
            //仅接受该对象所有者上报的输入，防止其他客户端伪造（-1 为服务器本地调用）
            int sender = NetObject.RpcSenderClientId;
            if (sender >= 0 && sender != NetObject.OwnerClientId) return;

            SendObserversRpc(nameof(OnReceiveInputFrame), ServerFrame, moveX, moveY, moveZ, jump);
        }
    }

    public struct InputFrame
    {

        public InputFrame(int authFrame, int moveX, int moveY, int moveZ, int jump)
        {
            AuthFrame = authFrame;
            MoveX = moveX;
            MoveY = moveY;
            MoveZ = moveZ;
            Jump = jump;
        }
        public int AuthFrame;
        public int MoveX;
        public int MoveY;
        public int MoveZ;
        public int Jump;

    }
}
