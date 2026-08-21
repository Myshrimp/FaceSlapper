using System;

namespace FaceSlapper.FrameSync
{
    /// <summary>帧同步全局配置与常量（各端必须一致，改动需全员同版本）。</summary>
    public static class FrameSyncConfig
    {
        /// <summary>逻辑帧率（Hz）。</summary>
        public const int TickRate = 30;

        /// <summary>
        /// 输入延迟（tick 数）。乐观预测模式下为 0：本地输入采样后立即上行并当帧自预测生效，
        /// 远端延迟由预测 + 回滚掩盖。协议层仍保留延迟参数，供离线测试使用。
        /// </summary>
        public const int InputDelayTicks = 0;

        /// <summary>状态哈希校验间隔（tick 数）。</summary>
        public const int HashIntervalTicks = 60;

        /// <summary>每 tick 时长（秒，仅用于本地节拍器，不进入模拟）。</summary>
        public const float TickSeconds = 1f / TickRate;

        /// <summary>每 tick 的定点步长（1/30，整数截断——各端常量一致即可）。</summary>
        public static readonly FP TickDelta = FP.FromRaw(FP.ONE / TickRate);

        /// <summary>竞技场可动范围（墙内缘 14.5 减去球半径 0.6 = 13.9）。</summary>
        public static readonly FP ArenaLimit = FP.FromRaw(139L * FP.ONE / 10);

        /// <summary>玩家碰撞半径（0.6 米）。</summary>
        public static readonly FP PlayerRadius = FP.FromRaw(6L * FP.ONE / 10);
    }

    /// <summary>帧输入按键位掩码。</summary>
    [Flags]
    public enum FrameButtons
    {
        None = 0,
        Attack = 1 << 0,
        Pickup = 1 << 1,
        Hitback = 1 << 2,
        Jump = 1 << 3,
        SpeedUp = 1 << 4,
    }

    /// <summary>
    /// 一个玩家一个 tick 的输入。移动轴按 1/64 量化为整数（[-64, 64] 对应 [-1, 1]），
    /// 全整数字段，序列化/传输/模拟零浮点误差。
    /// </summary>
    public struct FrameInput
    {
        public int Tick;
        public int ClientId;
        public int MoveX;
        public int MoveY;
        public int Buttons;

        public static FrameInput Empty(int tick, int clientId)
            => new FrameInput { Tick = tick, ClientId = clientId };

        public bool HasButton(FrameButtons b) => (Buttons & (int)b) != 0;

        /// <summary>量化移动轴还原为定点向量（模长不超过 1）。</summary>
        public FPVec2 MoveVector
            => new FPVec2(FP.FromRaw(MoveX * (FP.ONE / 64)), FP.FromRaw(MoveY * (FP.ONE / 64)));
    }

    /// <summary>本地浮点输入 → 量化整数（量化结果随输入广播，各端只使用量化后的值）。</summary>
    public static class InputQuantizer
    {
        public static int Quantize(float v)
        {
            if (v > 1f) v = 1f;
            else if (v < -1f) v = -1f;
            return (int)Math.Round(v * 64f, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>确定性随机数（xorshift32）：种子由服务器开局广播，禁用 UnityEngine.Random。</summary>
    public struct FrameRandom
    {
        private uint _state;

        public FrameRandom(int seed)
        {
            _state = (uint)seed;
            if (_state == 0) _state = 0x9E3779B9u;
        }

        public uint Next()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        public int Range(int minInclusive, int maxExclusive)
            => minInclusive + (int)(Next() % (uint)(maxExclusive - minInclusive));
    }

    /// <summary>单个玩家的确定性模拟状态（纯数据，与 MonoBehaviour 解耦，可离线自测）。</summary>
    public struct PlayerSimState
    {
        public FPVec3 Position;
        public FPVec2 Facing;
        public FP VelY;
        public bool Grounded;

        /// <summary>击退水平速度（每 tick 衰减）。</summary>
        public FPVec2 KnockVel;

        /// <summary>击退剩余 tick（期间操控力下降，禁止起跳）。</summary>
        public int KnockTicks;

        /// <summary>击飞技能冷却剩余 tick。</summary>
        public int CooldownTicks;

        /// <summary>攻击（巴掌）冷却剩余 tick。</summary>
        public int AttackCooldownTicks;

        /// <summary>挥击表现剩余 tick（渲染层读取；位于模拟状态内以保证各端一致）。</summary>
        public int SwingTicks;
    }
}
