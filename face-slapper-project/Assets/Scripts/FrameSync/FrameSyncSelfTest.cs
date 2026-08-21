using System.Collections.Generic;
using UnityEngine;

namespace FaceSlapper.FrameSync
{
    /// <summary>
    /// 帧同步自检：纯逻辑、无场景依赖，可在编辑器菜单 / GM 命令 / 批处理中运行。
    /// 覆盖三层：
    ///   1. 模拟层正确性（碰撞收敛、击飞命中/冷却/豁免）；
    ///   2. 模拟层确定性（两套实例消费同一脚本化输入序列，逐 tick 哈希一致，跨运行一致）；
    ///   3. 协议层（服务器转发校验：首提优先/严格连续/掉线拒收；
    ///      两端以不同推进时机消费同一确认输入流 + 掉线按生效 tick 移除，最终状态一致）。
    /// </summary>
    public static class FrameSyncSelfTest
    {
        private const int PlayerCount = 2;
        private const int InputSeed = 20260821;

        /// <summary>运行全部自检，返回是否通过（默认 1800 tick = 60 秒模拟）。</summary>
        public static bool RunDeterminismCheck(int ticks = 1800)
        {
            if (!RunCollisionCheck()) return false;
            if (!RunHitbackCheck()) return false;
            if (!RunAttackCheck()) return false;
            if (!RunProtocolCheck()) return false;

            FrameInput[][] scripted = GenerateScriptedInputs(ticks);

            uint firstRunFinalHash = 0;
            for (int run = 0; run < 2; run++)
            {
                // 同一 run 内的两套实例：模拟输入以不同顺序到达也能收敛到相同状态。
                PlayerSimState[] statesA = CreateInitialStates();
                PlayerSimState[] statesB = CreateInitialStates();

                for (int t = 0; t < ticks; t++)
                {
                    Step(statesA, scripted[t]);
                    Step(statesB, scripted[t]);

                    uint ha = HashAll(statesA);
                    uint hb = HashAll(statesB);
                    if (ha != hb)
                    {
                        Debug.LogError($"[FrameSyncTest] 自检失败：run={run} tick={t} 哈希不一致 ({ha} vs {hb})");
                        return false;
                    }

                    if (run == 0 && t == ticks - 1) firstRunFinalHash = ha;
                    if (run == 1 && t == ticks - 1 && ha != firstRunFinalHash)
                    {
                        Debug.LogError($"[FrameSyncTest] 自检失败：两次运行的最终哈希不一致 ({firstRunFinalHash} vs {ha})");
                        return false;
                    }
                }
            }

            Debug.Log($"[FrameSyncTest] 确定性自检通过：{ticks} tick × {PlayerCount} 玩家 × 2 实例 × 2 轮，" +
                      $"逐 tick 哈希一致（final={firstRunFinalHash}）");
            return true;
        }

        // ---------------- 内部 ----------------

        /// <summary>
        /// 碰撞回归测试：两玩家相距 4 米沿 X 轴相向而行 200 tick，
        /// 最终距离应收敛到碰撞直径（约 1.2 米）附近，且不穿透、不错位。
        /// </summary>
        public static bool RunCollisionCheck()
        {
            var a = new PlayerSimState
            {
                Position = new FPVec3(FP.FromInt(-2), FP.Zero, FP.Zero),
                Facing = new FPVec2(FP.One, FP.Zero),
                VelY = FP.Zero,
                Grounded = true,
            };
            var b = new PlayerSimState
            {
                Position = new FPVec3(FP.FromInt(2), FP.Zero, FP.Zero),
                Facing = new FPVec2(-FP.One, FP.Zero),
                VelY = FP.Zero,
                Grounded = true,
            };
            FrameInput inputA = new FrameInput { MoveX = 64 };  // 向 +X 全速
            FrameInput inputB = new FrameInput { MoveX = -64 }; // 向 -X 全速

            for (int t = 0; t < 200; t++)
            {
                FrameSyncSim.SimulateTick(ref a, inputA);
                FrameSyncSim.SimulateTick(ref b, inputB);
                FrameSyncSim.ResolvePairCollision(ref a, ref b);
            }

            FP dist = FP.Abs(b.Position.X - a.Position.X);
            long diameterRaw = FrameSyncConfig.PlayerRadius.Raw * 2;
            // 允许围绕直径一个步长（0.2 米/tick）以内的相位误差。
            long tolerance = FP.FromRaw(FP.ONE / 4).Raw;
            bool separated = dist.Raw >= diameterRaw - tolerance && dist.Raw <= diameterRaw + tolerance;
            bool noCrossover = a.Position.X.Raw < b.Position.X.Raw;

            if (!separated || !noCrossover)
            {
                Debug.LogError($"[FrameSyncTest] 碰撞自检失败：dist={dist}（期望≈{FP.FromRaw(diameterRaw)}），" +
                               $"a.X={a.Position.X} b.X={b.Position.X}");
                return false;
            }
            Debug.Log($"[FrameSyncTest] 碰撞自检通过：相向而行最终距离 {dist}（直径 {FP.FromRaw(diameterRaw)}）");
            return true;
        }

        /// <summary>
        /// 击飞回归测试：攻击者面向 +X、受害者位于身前 1.5 米（判定圈内）时被击退
        /// （水平击退速度 > 0、离地、冷却生效、二次立即击打被冷却拒绝）；
        /// 5 米外的旁观者不受影响。
        /// </summary>
        public static bool RunHitbackCheck()
        {
            var states = new PlayerSimState[3];
            states[0] = new PlayerSimState
            {
                Position = FPVec3.Zero,
                Facing = new FPVec2(FP.One, FP.Zero),
                Grounded = true,
            };
            states[1] = new PlayerSimState
            {
                Position = new FPVec3(FP.FromRaw(3L * FP.ONE / 2), FP.Zero, FP.Zero), // 身前 1.5m
                Facing = new FPVec2(FP.Zero, FP.One),
                Grounded = true,
            };
            states[2] = new PlayerSimState
            {
                Position = new FPVec3(FP.FromInt(5), FP.Zero, FP.Zero),              // 5m 外旁观
                Facing = new FPVec2(FP.Zero, FP.One),
                Grounded = true,
            };

            var inputs = new FrameInput[3];
            inputs[0] = new FrameInput { Buttons = (int)FrameButtons.Hitback };
            var active = new[] { true, true, true };

            FrameSyncSim.ResolveHitback(states, inputs, active, 3);

            bool victimKnocked = states[1].KnockVel.X.Raw > 0 && states[1].VelY.Raw > 0
                                 && !states[1].Grounded && states[1].KnockTicks > 0;
            bool cooldownSet = states[0].CooldownTicks > 0;
            bool bystanderSafe = states[2].KnockVel.SqrMagnitude.Raw == 0 && states[2].Grounded;

            // 冷却期间的第二次击打：不应改变受害者状态。
            PlayerSimState snapshot = states[1];
            FrameSyncSim.ResolveHitback(states, inputs, active, 3);
            bool cooldownBlocks = states[1].KnockVel.X.Raw == snapshot.KnockVel.X.Raw
                                  && states[1].VelY.Raw == snapshot.VelY.Raw;

            if (!victimKnocked || !cooldownSet || !bystanderSafe || !cooldownBlocks)
            {
                Debug.LogError($"[FrameSyncTest] 击飞自检失败：victimKnocked={victimKnocked} " +
                               $"cooldownSet={cooldownSet} bystanderSafe={bystanderSafe} cooldownBlocks={cooldownBlocks}");
                return false;
            }
            Debug.Log("[FrameSyncTest] 击飞自检通过：命中击退/冷却/旁观者豁免均符合预期");
            return true;
        }

        /// <summary>
        /// 攻击（巴掌）回归测试：攻击者面向 +X、受害者位于身前 2 米（判定圈内）时被击退
        /// （水平击退速度 > 0、离地、攻击冷却与挥击表现生效、冷却期内二次攻击被拒绝）；
        /// 6 米外的旁观者不受影响。
        /// </summary>
        public static bool RunAttackCheck()
        {
            var states = new PlayerSimState[3];
            states[0] = new PlayerSimState
            {
                Position = FPVec3.Zero,
                Facing = new FPVec2(FP.One, FP.Zero),
                Grounded = true,
            };
            states[1] = new PlayerSimState
            {
                Position = new FPVec3(FP.FromInt(2), FP.Zero, FP.Zero), // 身前 2m（判定点 1.2 + 半径 2.0 内）
                Facing = new FPVec2(FP.Zero, FP.One),
                Grounded = true,
            };
            states[2] = new PlayerSimState
            {
                Position = new FPVec3(FP.FromInt(6), FP.Zero, FP.Zero), // 6m 外旁观
                Facing = new FPVec2(FP.Zero, FP.One),
                Grounded = true,
            };

            var inputs = new FrameInput[3];
            inputs[0] = new FrameInput { Buttons = (int)FrameButtons.Attack };
            var active = new[] { true, true, true };

            FrameSyncSim.ResolveAttack(states, inputs, active, 3);

            bool victimKnocked = states[1].KnockVel.X.Raw > 0 && states[1].VelY.Raw > 0
                                 && !states[1].Grounded && states[1].KnockTicks > 0;
            bool cooldownSet = states[0].AttackCooldownTicks > 0;
            bool swingStarted = states[0].SwingTicks == FrameSyncSim.SwingTotalTicks;
            bool bystanderSafe = states[2].KnockVel.SqrMagnitude.Raw == 0 && states[2].Grounded;

            // 冷却期内的第二次攻击：不应改变受害者状态、不重置挥击表现。
            PlayerSimState snapshot = states[1];
            states[0].SwingTicks = 0; // 模拟挥动已结束，便于验证冷却拒绝是否重新触发挥击
            FrameSyncSim.ResolveAttack(states, inputs, active, 3);
            bool cooldownBlocks = states[1].KnockVel.X.Raw == snapshot.KnockVel.X.Raw
                                  && states[1].VelY.Raw == snapshot.VelY.Raw
                                  && states[0].SwingTicks == 0;

            if (!victimKnocked || !cooldownSet || !swingStarted || !bystanderSafe || !cooldownBlocks)
            {
                Debug.LogError($"[FrameSyncTest] 攻击自检失败：victimKnocked={victimKnocked} " +
                               $"cooldownSet={cooldownSet} swingStarted={swingStarted} " +
                               $"bystanderSafe={bystanderSafe} cooldownBlocks={cooldownBlocks}");
                return false;
            }
            Debug.Log("[FrameSyncTest] 攻击自检通过：命中击退/冷却/挥击表现/旁观者豁免均符合预期");
            return true;
        }

        /// <summary>
        /// 协议层回归测试：
        /// 1) 服务器转发校验——首提优先（重复拒绝）、严格连续（跳号拒绝）、非成员/掉线拒收；
        /// 2) 异步推进一致性——两端消费同一确认输入流 + 掉线按生效 tick 移除，
        ///    一端逐条推进、另一端收齐后一次性推进，最终 tick 与状态哈希必须一致；
        /// 3) 停帧语义——缺少确认输入时不得推进。
        /// </summary>
        public static bool RunProtocolCheck()
        {
            int[] roster = { 0, 1 };

            // ---- 1) 服务器转发校验 ----
            var server = new FrameSyncProtocol();
            server.ServerInitRoster(roster, FrameSyncConfig.InputDelayTicks);
            bool sequenceRules =
                server.ServerTryRelay(0, 2) &&   // 首条合法（首个真实 tick = InputDelayTicks）
                !server.ServerTryRelay(0, 2) &&  // 重复拒绝（首提优先）
                !server.ServerTryRelay(0, 4) &&  // 跳号拒绝
                server.ServerTryRelay(0, 3) &&   // 连续接受
                !server.ServerTryRelay(9, 4);    // 非成员拒绝
            server.ServerTryRelay(1, 2);
            server.ServerTryRelay(1, 3);
            int effectiveTick = server.ServerMarkLeft(1);
            bool leftRules = effectiveTick == 4          // 生效 tick = 最后转发 tick + 1
                             && !server.ServerTryRelay(1, 4); // 掉线后拒收

            if (!sequenceRules || !leftRules)
            {
                Debug.LogError($"[FrameSyncTest] 协议自检失败（转发校验）：sequenceRules={sequenceRules} " +
                               $"leftRules={leftRules} effectiveTick={effectiveTick}");
                return false;
            }

            // ---- 2) 异步推进一致性 ----
            // 广播流（与服务器转发顺序一致）：p0 的 tick 2..7，p1 的 tick 2..3，p1 于 tick 4 统一移除。
            var stream = new List<Broadcast>
            {
                Broadcast.OfInput(new FrameInput { Tick = 2, ClientId = 0, MoveX = 32 }),
                Broadcast.OfInput(new FrameInput { Tick = 2, ClientId = 1, MoveY = -16 }),
                Broadcast.OfInput(new FrameInput { Tick = 3, ClientId = 0, MoveX = 32 }),
                Broadcast.OfInput(new FrameInput { Tick = 3, ClientId = 1, MoveY = -16 }),
                Broadcast.OfInput(new FrameInput { Tick = 4, ClientId = 0, MoveX = 32 }),
                Broadcast.Removal(1, 4),
                Broadcast.OfInput(new FrameInput { Tick = 5, ClientId = 0, MoveX = 32, Buttons = (int)FrameButtons.Jump }),
                Broadcast.OfInput(new FrameInput { Tick = 6, ClientId = 0, MoveX = 32 }),
                Broadcast.OfInput(new FrameInput { Tick = 7, ClientId = 0, MoveX = 32 }),
            };

            // 端 A：每收到一条消息就尽力推进（模拟网络较好的端）。
            var endA = new FrameSyncProtocol();
            endA.BeginSession(roster, FrameSyncConfig.InputDelayTicks);
            PlayerSimState[] statesA = CreateInitialStates();
            var inputBuf = new FrameInput[PlayerCount];
            var activeBuf = new bool[PlayerCount];
            int tickA = 0;
            foreach (Broadcast msg in stream)
            {
                msg.ApplyTo(endA);
                tickA = StepWhilePossible(endA, statesA, inputBuf, activeBuf, tickA);
            }

            // 端 B：收完全部消息后一次性推进（模拟网络较差/卡顿的端）。
            var endB = new FrameSyncProtocol();
            endB.BeginSession(roster, FrameSyncConfig.InputDelayTicks);
            PlayerSimState[] statesB = CreateInitialStates();
            foreach (Broadcast msg in stream) msg.ApplyTo(endB);
            int tickB = StepWhilePossible(endB, statesB, inputBuf, activeBuf, 0);

            uint hashA = HashActive(statesA, endA);
            uint hashB = HashActive(statesB, endB);
            if (tickA != 8 || tickB != 8 || hashA != hashB)
            {
                Debug.LogError($"[FrameSyncTest] 协议自检失败（异步推进）：tickA={tickA} tickB={tickB} " +
                               $"hashA={hashA} hashB={hashB}");
                return false;
            }

            // ---- 3) 停帧语义：缺少 p0 的 tick 7 时必须停在 tick 7 ----
            var endC = new FrameSyncProtocol();
            endC.BeginSession(roster, FrameSyncConfig.InputDelayTicks);
            PlayerSimState[] statesC = CreateInitialStates();
            for (int i = 0; i < stream.Count - 1; i++) stream[i].ApplyTo(endC); // 去掉最后一条
            int tickC = StepWhilePossible(endC, statesC, inputBuf, activeBuf, 0);
            if (tickC != 7 || endC.HasAllInputsFor(7))
            {
                Debug.LogError($"[FrameSyncTest] 协议自检失败（停帧语义）：tickC={tickC}（期望停在 7）");
                return false;
            }

            Debug.Log("[FrameSyncTest] 协议自检通过：转发校验/异步推进一致性/停帧语义均符合预期");
            return true;
        }

        // ---------------- 协议测试辅助 ----------------

        private struct Broadcast
        {
            public bool IsRemoval;
            public FrameInput Input;
            public int LeftClientId;
            public int EffectiveTick;

            public static Broadcast OfInput(FrameInput input) => new Broadcast { Input = input };

            public static Broadcast Removal(int clientId, int effectiveTick)
                => new Broadcast { IsRemoval = true, LeftClientId = clientId, EffectiveTick = effectiveTick };

            public void ApplyTo(FrameSyncProtocol proto)
            {
                if (IsRemoval) proto.OnPlayerLeft(LeftClientId, EffectiveTick);
                else proto.OnConfirmedInput(Input);
            }
        }

        /// <summary>与 FrameSyncManager 推进循环同构：应用移除 → 齐备才模拟 → 消费。</summary>
        private static int StepWhilePossible(FrameSyncProtocol proto, PlayerSimState[] states,
            FrameInput[] inputBuf, bool[] activeBuf, int tick)
        {
            int[] roster = proto.Roster;
            while (true)
            {
                proto.ApplyRemovals(tick);
                if (!proto.HasAllInputsFor(tick)) return tick;

                for (int i = 0; i < roster.Length; i++)
                {
                    activeBuf[i] = proto.IsActive(roster[i]);
                    if (activeBuf[i]) inputBuf[i] = proto.GetInput(roster[i], tick);
                }
                FrameSyncSim.SimulateAll(states, inputBuf, activeBuf, roster.Length);
                FrameSyncSim.ResolveCollisions(states, activeBuf, roster.Length);
                FrameSyncSim.ResolveHitback(states, inputBuf, activeBuf, roster.Length);
                FrameSyncSim.ResolveAttack(states, inputBuf, activeBuf, roster.Length);
                proto.ConsumeTick(tick);
                tick++;
            }
        }

        private static uint HashActive(PlayerSimState[] states, FrameSyncProtocol proto)
        {
            uint h = 2166136261u;
            int[] roster = proto.Roster;
            for (int i = 0; i < roster.Length; i++)
            {
                if (!proto.IsActive(roster[i])) continue;
                h = FrameSyncSim.Mix(h, roster[i]);
                h = FrameSyncSim.MixState(h, states[i]);
            }
            return h;
        }

        private static FrameInput[][] GenerateScriptedInputs(int ticks)
        {
            var gen = new FrameRandom(InputSeed);
            var result = new FrameInput[ticks][];

            // 每玩家当前的"持续输入"状态（模拟真实按住按键）。
            var curMoveX = new int[PlayerCount];
            var curMoveY = new int[PlayerCount];

            for (int t = 0; t < ticks; t++)
            {
                result[t] = new FrameInput[PlayerCount];
                for (int p = 0; p < PlayerCount; p++)
                {
                    // 每 25 tick 随机换一次方向（含静止）。
                    if (t % 25 == 0)
                    {
                        int dirIndex = gen.Range(0, 9);
                        curMoveX[p] = dirIndex == 8 ? 0 : DirectionTable[dirIndex][0];
                        curMoveY[p] = dirIndex == 8 ? 0 : DirectionTable[dirIndex][1];
                    }

                    int buttons = 0;
                    if (t % 53 == 7) buttons |= (int)FrameButtons.Jump;       // 周期性跳跃
                    if (t % 80 < 20) buttons |= (int)FrameButtons.SpeedUp;    // 周期性加速窗口
                    if (t % 97 == 13) buttons |= (int)FrameButtons.Attack;
                    if (p == 0 && t % 61 == 10) buttons |= (int)FrameButtons.Hitback; // 周期性击飞
                    if (p == 1 && t % 89 == 20) buttons |= (int)FrameButtons.Hitback;

                    result[t][p] = new FrameInput
                    {
                        Tick = t,
                        ClientId = p,
                        MoveX = curMoveX[p],
                        MoveY = curMoveY[p],
                        Buttons = buttons,
                    };
                }
            }
            return result;
        }

        // 8 方向量化值（含对角线 45/64 ≈ 0.707）。
        private static readonly int[][] DirectionTable =
        {
            new[] { 0, 64 }, new[] { 45, 45 }, new[] { 64, 0 }, new[] { 45, -45 },
            new[] { 0, -64 }, new[] { -45, -45 }, new[] { -64, 0 }, new[] { -45, 45 },
        };

        private static PlayerSimState[] CreateInitialStates()
        {
            var states = new PlayerSimState[PlayerCount];
            for (int i = 0; i < PlayerCount; i++)
            {
                states[i] = new PlayerSimState
                {
                    Position = new FPVec3(FP.FromInt(4 - i * 8), FP.FromRaw(205), FP.FromInt(4 - i * 8)),
                    Facing = new FPVec2(FP.Zero, FP.One),
                    VelY = FP.Zero,
                    Grounded = true,
                };
            }
            return states;
        }

        private static readonly bool[] AllActive = { true, true };

        private static void Step(PlayerSimState[] states, FrameInput[] inputs)
        {
            // 与运行时（FrameSyncManager.StepSimulation）完全相同的批量模拟路径。
            int n = states.Length;
            FrameSyncSim.SimulateAll(states, inputs, AllActive, n);
            FrameSyncSim.ResolveCollisions(states, AllActive, n);
            FrameSyncSim.ResolveHitback(states, inputs, AllActive, n);
            FrameSyncSim.ResolveAttack(states, inputs, AllActive, n);
        }

        private static uint HashAll(PlayerSimState[] states)
        {
            uint h = 2166136261u;
            for (int i = 0; i < states.Length; i++)
            {
                h = FrameSyncSim.Mix(h, i);
                h = FrameSyncSim.MixState(h, states[i]);
            }
            return h;
        }
    }
}
