namespace FaceSlapper.FrameSync
{
    /// <summary>
    /// 确定性模拟核心：纯静态方法 + 纯数据状态，不依赖 MonoBehaviour/Unity 物理。
    /// 同一初始状态 + 同一输入序列 → 任意端逐 tick 结果 bitwise 一致。
    /// 模拟规则（改动即破坏同步，需全员同版本）：
    ///   1. 每 tick 固定步长 1/30；
    ///   2. 每 tick 顺序：移动 → 圆形碰撞 → 击飞判定；玩家遍历一律按 clientId 升序；
    ///   3. 只使用定点数运算；阈值比较必须用 FP 乘法还原量纲（严禁直接比较 raw 平方）。
    /// </summary>
    public static class FrameSyncSim
    {
        private static readonly FP MoveSpeed = FP.FromInt(6);
        private static readonly FP SpeedUpMultiplier = FP.FromRaw(3L * FP.ONE / 2); // 1.5x
        private static readonly FP JumpSpeed = FP.FromInt(7);
        private static readonly FP Gravity = FP.FromInt(20);
        private static readonly FP TurnLerp = FP.FromRaw(2L * FP.ONE / 5);          // 每 tick 朝向插值 0.4
        private static readonly FP Half = FP.FromRaw(FP.ONE / 2);
        private static readonly FP Quarter = FP.FromRaw(FP.ONE / 4);

        // 击飞（对齐状态同步版 HitbackAbility：冷却 3s、前方 2.2m、半径 1.1、力度 8、恢复 0.35s）。
        private static readonly FP HitbackRangeHalf = FP.FromRaw(11L * FP.ONE / 10); // 1.1 = 2.2/2
        private static readonly FP HitbackRadius = FP.FromRaw(11L * FP.ONE / 10);    // 1.1
        private static readonly FP HitbackForce = FP.FromInt(8);
        private static readonly FP KnockControlFactor = FP.FromRaw(3L * FP.ONE / 10); // 击退中 30% 操控
        private static readonly FP KnockDecay = FP.FromRaw(9L * FP.ONE / 10);         // 击退速度每 tick 衰减
        private const int HitbackCooldownTicks = 90;  // 3s * 30Hz
        private const int KnockRecoverTicks = 11;     // 0.35s * 30Hz ≈ 10.5，取 11

        // ---------------- 批处理入口（运行时已收集好的对齐数组，roster 顺序） ----------------

        /// <summary>逐玩家推进移动/跳跃/击退积分。states/inputs/active 按下标对齐。</summary>
        public static void SimulateAll(PlayerSimState[] states, FrameInput[] inputs, bool[] active, int count)
        {
            for (int i = 0; i < count; i++)
                if (active[i]) SimulateTick(ref states[i], inputs[i]);
        }

        /// <summary>玩家间圆形碰撞：i&lt;j 成对解算，顺序确定。</summary>
        public static void ResolveCollisions(PlayerSimState[] states, bool[] active, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!active[i]) continue;
                for (int j = i + 1; j < count; j++)
                {
                    if (!active[j]) continue;
                    ResolvePairCollision(ref states[i], ref states[j]);
                }
            }
        }

        /// <summary>
        /// 击飞判定：攻击者按序处理；命中判定以"身前 RangeHalf 处为圆心、Radius+玩家半径为距"的圆形区域；
        /// 同一 tick 多名攻击者时后序攻击者覆盖先序的击退效果（确定性规则）。
        /// </summary>
        public static void ResolveHitback(PlayerSimState[] states, FrameInput[] inputs, bool[] active, int count)
        {
            FP hitDist = HitbackRadius + FrameSyncConfig.PlayerRadius;
            FP sqrHitDist = hitDist * hitDist;

            for (int i = 0; i < count; i++)
            {
                if (!active[i] || !inputs[i].HasButton(FrameButtons.Hitback)) continue;
                if (states[i].CooldownTicks > 0) continue;
                states[i].CooldownTicks = HitbackCooldownTicks;

                // 判定点：身前 RangeHalf 处（仅 XZ 平面，跳跃高度不参与判定——与原 OverlapSphere 近似的简化）。
                FPVec2 center = new FPVec2(
                    states[i].Position.X + states[i].Facing.X * HitbackRangeHalf,
                    states[i].Position.Z + states[i].Facing.Y * HitbackRangeHalf);

                for (int j = 0; j < count; j++)
                {
                    if (j == i || !active[j]) continue;

                    FP dx = states[j].Position.X - center.X;
                    FP dz = states[j].Position.Z - center.Y;
                    FP sqrDist = dx * dx + dz * dz;
                    if (sqrDist.Raw >= sqrHitDist.Raw) continue;

                    ApplyKnockback(ref states[j], states[i]);
                }
            }
        }

        /// <summary>对受害者施加击退：方向 = 攻击者→受害者（完全重合时用攻击者朝向），上挑比 0.5。</summary>
        private static void ApplyKnockback(ref PlayerSimState victim, PlayerSimState attacker)
        {
            FP dx = victim.Position.X - attacker.Position.X;
            FP dz = victim.Position.Z - attacker.Position.Z;
            FP sqrDist = dx * dx + dz * dz;

            FPVec2 dir;
            if (sqrDist.Raw <= 0) dir = attacker.Facing;
            else
            {
                FP dist = FP.Sqrt(sqrDist);
                dir = new FPVec2(dx / dist, dz / dist);
            }

            // 冲量 = (dir + up*0.5) 归一化 × 力度（等价于状态同步版的 ForceMode.VelocityChange）。
            FP horizMag = HitbackForce / FP.Sqrt(FP.One + Quarter);
            victim.KnockVel = dir * horizMag;
            victim.VelY = horizMag * Half;
            victim.KnockTicks = KnockRecoverTicks;
            victim.Grounded = false;
        }

        // ---------------- 单玩家逐 tick 模拟 ----------------

        /// <summary>推进一个玩家一个 tick（移动/跳跃/击退积分/朝向/边界）。</summary>
        public static void SimulateTick(ref PlayerSimState s, FrameInput input)
        {
            FP dt = FrameSyncConfig.TickDelta;

            if (s.CooldownTicks > 0) s.CooldownTicks--;

            // 水平移动（击退中操控力降为 30%，保留击退手感）。
            FPVec2 dir = input.MoveVector;
            FP speed = input.HasButton(FrameButtons.SpeedUp) ? MoveSpeed * SpeedUpMultiplier : MoveSpeed;
            if (s.KnockTicks > 0)
            {
                s.KnockTicks--;
                speed = speed * KnockControlFactor;
            }
            FPVec2 step = dir * (speed * dt);
            s.Position = new FPVec3(s.Position.X + step.X, s.Position.Y, s.Position.Z + step.Y);

            // 击退速度积分与衰减。
            if (s.KnockVel.SqrMagnitude.Raw > 0)
            {
                s.Position = new FPVec3(
                    s.Position.X + s.KnockVel.X * dt,
                    s.Position.Y,
                    s.Position.Z + s.KnockVel.Y * dt);
                s.KnockVel = s.KnockVel * KnockDecay;
                // 足够小则归零，避免长尾漂移。
                if (s.KnockVel.SqrMagnitude.Raw < FP.FromRaw(FP.ONE / 64).Raw)
                    s.KnockVel = FPVec2.Zero;
            }

            // 跳跃与重力（简化运动学；击退硬直中禁止起跳）。
            if (input.HasButton(FrameButtons.Jump) && s.Grounded && s.KnockTicks <= 0)
            {
                s.VelY = JumpSpeed;
                s.Grounded = false;
            }
            if (!s.Grounded)
            {
                s.VelY = s.VelY - Gravity * dt;
                FP y = s.Position.Y + s.VelY * dt;
                if (y.Raw <= 0)
                {
                    y = FP.Zero;
                    s.VelY = FP.Zero;
                    s.Grounded = true;
                }
                s.Position = new FPVec3(s.Position.X, y, s.Position.Z);
            }

            // 朝向：确定性插值 + 归一化（无三角函数）。
            if (dir.SqrMagnitude.Raw > 0)
            {
                FPVec2 target = dir.Normalized;
                s.Facing = FPVec2.Lerp(s.Facing, target, TurnLerp).Normalized;
                if (s.Facing.SqrMagnitude.Raw == 0) s.Facing = target;
            }

            ClampToArena(ref s);
        }

        /// <summary>
        /// 两个玩家的圆形碰撞解算（XZ 平面）：重叠时沿法线各推一半。
        /// 调用方必须保证遍历顺序确定（按 clientId 升序、i&lt;j 成对调用）。
        /// </summary>
        public static void ResolvePairCollision(ref PlayerSimState a, ref PlayerSimState b)
        {
            FP diameter = FrameSyncConfig.PlayerRadius + FrameSyncConfig.PlayerRadius;

            // 高度错开（一方跳起/被击飞）时不挤压。
            FP dy = FP.Abs(a.Position.Y - b.Position.Y);
            if (dy.Raw >= diameter.Raw) return;

            FP dx = b.Position.X - a.Position.X;
            FP dz = b.Position.Z - a.Position.Z;
            FP sqrDist = dx * dx + dz * dz;
            // 注意：阈值必须用 FP 乘法（右移还原量纲），不能直接比较 raw 的平方。
            FP sqrDiameter = diameter * diameter;
            if (sqrDist.Raw >= sqrDiameter.Raw) return;

            FP dist = FP.Sqrt(sqrDist);
            FPVec2 normal;
            if (dist.Raw <= 0)
            {
                // 完全重合：使用确定性固定方向（低 clientId 一方为 a，向 -X 推）。
                normal = new FPVec2(FP.One, FP.Zero);
            }
            else
            {
                normal = new FPVec2(dx / dist, dz / dist);
            }

            FP push = (diameter - dist) * Half;
            FPVec2 offset = normal * push;
            a.Position = new FPVec3(a.Position.X - offset.X, a.Position.Y, a.Position.Z - offset.Y);
            b.Position = new FPVec3(b.Position.X + offset.X, b.Position.Y, b.Position.Z + offset.Y);
            ClampToArena(ref a);
            ClampToArena(ref b);
        }

        private static void ClampToArena(ref PlayerSimState s)
        {
            FP limit = FrameSyncConfig.ArenaLimit;
            s.Position = new FPVec3(
                FP.Clamp(s.Position.X, -limit, limit),
                s.Position.Y,
                FP.Clamp(s.Position.Z, -limit, limit));
        }

        // ---------------- 状态哈希（FNV-1a 32bit，不同步检测用） ----------------

        public static uint Mix(uint h, long v)
        {
            unchecked
            {
                h ^= (uint)v;
                h *= 16777619u;
                h ^= (uint)(v >> 32);
                h *= 16777619u;
                return h;
            }
        }

        /// <summary>把一个玩家的模拟状态混入哈希。调用方保证遍历顺序确定。</summary>
        public static uint MixState(uint h, PlayerSimState s)
        {
            h = Mix(h, s.Position.X.Raw);
            h = Mix(h, s.Position.Y.Raw);
            h = Mix(h, s.Position.Z.Raw);
            h = Mix(h, s.Facing.X.Raw);
            h = Mix(h, s.Facing.Y.Raw);
            h = Mix(h, s.VelY.Raw);
            h = Mix(h, s.Grounded ? 1 : 0);
            h = Mix(h, s.KnockVel.X.Raw);
            h = Mix(h, s.KnockVel.Y.Raw);
            h = Mix(h, s.KnockTicks);
            h = Mix(h, s.CooldownTicks);
            return h;
        }
    }
}
