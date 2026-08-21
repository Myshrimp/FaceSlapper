using System;

namespace FaceSlapper.FrameSync
{
    /// <summary>
    /// 定点数（int64 定点，精度 1/4096）：帧同步确定性模拟的数学基础。
    /// 所有参与模拟状态的运算只允许使用 FP/FPVec2/FPVec3，禁止 float/double 混入，
    /// 禁止 Mathf/UnityEngine.Random（渲染层除外，渲染不参与状态）。
    /// </summary>
    public struct FP : IComparable<FP>
    {
        public const int SHIFT = 12;
        public const long ONE = 1L << SHIFT;

        public static readonly FP Zero = FromRaw(0);
        public static readonly FP One = FromRaw(ONE);

        public long Raw;

        public static FP FromRaw(long raw) { FP v; v.Raw = raw; return v; }
        public static FP FromInt(int v) => FromRaw((long)v << SHIFT);

        /// <summary>仅用于服务器初始化/配置换算（结果以 Raw 广播），模拟过程中禁止调用。</summary>
        public static FP FromFloat(float v) => FromRaw((long)Math.Round(v * ONE));

        public float ToFloat() => Raw / (float)ONE;

        public static FP operator +(FP a, FP b) => FromRaw(a.Raw + b.Raw);
        public static FP operator -(FP a, FP b) => FromRaw(a.Raw - b.Raw);
        public static FP operator -(FP a) => FromRaw(-a.Raw);
        public static FP operator *(FP a, FP b) => FromRaw((a.Raw * b.Raw) >> SHIFT);
        public static FP operator /(FP a, FP b) => FromRaw((a.Raw << SHIFT) / b.Raw);

        public static bool operator ==(FP a, FP b) => a.Raw == b.Raw;
        public static bool operator !=(FP a, FP b) => a.Raw != b.Raw;
        public static bool operator <(FP a, FP b) => a.Raw < b.Raw;
        public static bool operator >(FP a, FP b) => a.Raw > b.Raw;
        public static bool operator <=(FP a, FP b) => a.Raw <= b.Raw;
        public static bool operator >=(FP a, FP b) => a.Raw >= b.Raw;

        public bool Equals(FP other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is FP other && Equals(other);
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(FP other) => Raw.CompareTo(other.Raw);

        public static FP Abs(FP v) => v.Raw < 0 ? -v : v;
        public static FP Min(FP a, FP b) => a.Raw <= b.Raw ? a : b;
        public static FP Max(FP a, FP b) => a.Raw >= b.Raw ? a : b;
        public static FP Clamp(FP v, FP min, FP max) => v < min ? min : (v > max ? max : v);
        public static FP Lerp(FP a, FP b, FP t) => a + (b - a) * t;

        /// <summary>开方：整数牛顿迭代，无浮点，跨端结果一致。</summary>
        public static FP Sqrt(FP v)
        {
            if (v.Raw <= 0) return Zero;
            return FromRaw(IntSqrt(v.Raw << SHIFT));
        }

        private static long IntSqrt(long v)
        {
            if (v <= 0) return 0;
            long x = v;
            long y = (x + 1) >> 1;
            while (y < x)
            {
                x = y;
                y = (x + v / x) >> 1;
            }
            return x;
        }

        public override string ToString() => ToFloat().ToString("F4");
    }

    /// <summary>定点二维向量（移动/朝向用，XZ 平面映射为 X/Y 分量）。</summary>
    public struct FPVec2
    {
        public FP X;
        public FP Y;

        public FPVec2(FP x, FP y) { X = x; Y = y; }

        public static readonly FPVec2 Zero = new FPVec2(FP.Zero, FP.Zero);

        public FP SqrMagnitude => X * X + Y * Y;
        public FP Magnitude => FP.Sqrt(SqrMagnitude);

        public FPVec2 Normalized
        {
            get
            {
                FP m = Magnitude;
                return m.Raw <= 0 ? Zero : new FPVec2(X / m, Y / m);
            }
        }

        public static FPVec2 operator +(FPVec2 a, FPVec2 b) => new FPVec2(a.X + b.X, a.Y + b.Y);
        public static FPVec2 operator -(FPVec2 a, FPVec2 b) => new FPVec2(a.X - b.X, a.Y - b.Y);
        public static FPVec2 operator *(FPVec2 v, FP s) => new FPVec2(v.X * s, v.Y * s);

        public static FPVec2 Lerp(FPVec2 a, FPVec2 b, FP t) => a + (b - a) * t;

        public override string ToString() => $"({X}, {Y})";
    }

    /// <summary>定点三维向量（位置用，Y 为竖直方向）。</summary>
    public struct FPVec3
    {
        public FP X;
        public FP Y;
        public FP Z;

        public FPVec3(FP x, FP y, FP z) { X = x; Y = y; Z = z; }

        public static readonly FPVec3 Zero = new FPVec3(FP.Zero, FP.Zero, FP.Zero);

        public static FPVec3 operator +(FPVec3 a, FPVec3 b) => new FPVec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static FPVec3 operator -(FPVec3 a, FPVec3 b) => new FPVec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static FPVec3 operator *(FPVec3 v, FP s) => new FPVec3(v.X * s, v.Y * s, v.Z * s);

        public override string ToString() => $"({X}, {Y}, {Z})";
    }
}
