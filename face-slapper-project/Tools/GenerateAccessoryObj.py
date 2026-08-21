# -*- coding: utf-8 -*-
"""
程序化生成脸部配饰 OBJ 模型文件（无需外部建模工具）：
  - Beard.obj            大胡子（贴合球脸的弧面壳 + 锯齿状下摆）
  - Glasses_Circle.obj   圆形眼镜
  - Glasses_Square.obj   方形眼镜
  - Glasses_Star.obj     星形眼镜
  - Glasses_Heart.obj    心形眼镜

所有模型都在 Player 根节点本地空间建模（脸球心 (0, 0.6, 0)，半径 0.6，
眼睛在 (±0.13, 0.72, 0.575)），作为 Player 子节点以 identity 变换挂上即可。
输出目录：Assets/Art/Meshes/Accessories/，Unity 会自动导入 OBJ。

用法：python Tools/GenerateAccessoryObj.py   （在工程根目录执行，幂等可重复）
"""
import math
import os

OUT_DIR = os.path.join("Assets", "Art", "Meshes", "Accessories")

# ---------------- 向量小工具 ----------------

def vsub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])

def vcross(a, b):
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])

def vdot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]

def vnorm(a):
    l = math.sqrt(vdot(a, a)) or 1.0
    return (a[0] / l, a[1] / l, a[2] / l)

def face_normal(pts):
    n = (0.0, 0.0, 0.0)
    for i in range(len(pts)):
        p, q = pts[i], pts[(i + 1) % len(pts)]
        n = (n[0] + (p[1] - q[1]) * (p[2] + q[2]),
             n[1] + (p[2] - q[2]) * (p[0] + q[0]),
             n[2] + (p[0] - q[0]) * (p[1] + q[1]))
    return vnorm(n)

class ObjWriter:
    """平面着色：每个面独立顶点，法线 = 面法线（低模卡通风格）。"""
    def __init__(self, name):
        self.name = name
        self.verts = []
        self.norms = []
        self.faces = []

    def add_face(self, pts, desired_dir=None, double_sided=False):
        """添加一个多边形面。desired_dir 给定期望朝向，用于自动修正绕序。"""
        n = face_normal(pts)
        if desired_dir is not None and vdot(n, desired_dir) < 0:
            pts = list(reversed(pts))
            n = (-n[0], -n[1], -n[2])
        self._emit(pts, n)
        if double_sided:
            self._emit(list(reversed(pts)), (-n[0], -n[1], -n[2]))

    def _emit(self, pts, n):
        base_v = len(self.verts) + 1
        self.verts.extend(pts)
        self.norms.append(n)
        ni = len(self.norms)
        self.faces.append([(base_v + i, ni) for i in range(len(pts))])

    def add_box(self, center, size, right=(1, 0, 0), up=(0, 1, 0), fwd=(0, 0, 1),
                double_sided=False):
        """添加一个盒子（center 中心，size 全尺寸，按 right/up/fwd 基向量定向）。"""
        hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
        def corner(sx, sy, sz):
            return (center[0] + right[0] * sx * hx + up[0] * sy * hy + fwd[0] * sz * hz,
                    center[1] + right[1] * sx * hx + up[1] * sy * hy + fwd[1] * sz * hz,
                    center[2] + right[2] * sx * hx + up[2] * sy * hy + fwd[2] * sz * hz)
        quads = [
            ([corner(1, -1, -1), corner(1, -1, 1), corner(1, 1, 1), corner(1, 1, -1)], right),
            ([corner(-1, -1, -1), corner(-1, -1, 1), corner(-1, 1, 1), corner(-1, 1, -1)],
             (-right[0], -right[1], -right[2])),
            ([corner(-1, 1, -1), corner(-1, 1, 1), corner(1, 1, 1), corner(1, 1, -1)], up),
            ([corner(-1, -1, -1), corner(-1, -1, 1), corner(1, -1, 1), corner(1, -1, -1)],
             (-up[0], -up[1], -up[2])),
            ([corner(-1, -1, 1), corner(-1, 1, 1), corner(1, 1, 1), corner(1, -1, 1)], fwd),
            ([corner(-1, -1, -1), corner(-1, 1, -1), corner(1, 1, -1), corner(1, -1, -1)],
             (-fwd[0], -fwd[1], -fwd[2])),
        ]
        for pts, d in quads:
            self.add_face(pts, d, double_sided)

    def write(self, path):
        with open(path, "w", encoding="utf-8") as f:
            f.write("# FaceSlapper procedural accessory - %s\n" % self.name)
            f.write("# Player-root local space; face sphere center (0, 0.6, 0) r=0.6\n")
            f.write("o %s\n" % self.name)
            for v in self.verts:
                f.write("v %.6f %.6f %.6f\n" % v)
            for n in self.norms:
                f.write("vn %.4f %.4f %.4f\n" % n)
            for face in self.faces:
                f.write("f " + " ".join("%d//%d" % idx for idx in face) + "\n")
        print("  %-22s %5d verts, %4d faces -> %s"
              % (self.name, len(self.verts), len(self.faces), path))

# ---------------- 大胡子 ----------------

FACE_CENTER = (0.0, 0.6, 0.0)
FACE_RADIUS = 0.6

def sphere_point(phi, psi, r):
    """phi: 绕 Y 的方位角（0=脸正前方 +Z）；psi: 仰角（负值朝下）。"""
    cp = math.cos(psi)
    return (FACE_CENTER[0] + r * cp * math.sin(phi),
            FACE_CENTER[1] + r * math.sin(psi),
            FACE_CENTER[2] + r * cp * math.cos(phi))

def build_beard():
    """大胡子：包住下半张脸的弧面壳，向外鼓出，下摆锯齿状。"""
    w = ObjWriter("Beard")
    n_phi, n_psi = 16, 7
    phi_max = math.radians(72)          # 覆盖脸颊两侧
    psi_top = math.radians(-4)          # 上沿在眼睛下方
    psi_bot = math.radians(-74)         # 下沿接近下巴/脖子

    def bot_angle(i):
        # 锯齿下摆：奇偶列深浅交替，模拟一撮撮胡须尖
        return psi_bot + (math.radians(-9) if i % 2 == 0 else 0.0)

    def outer_radius(t):
        return 0.620 + 0.050 * (t ** 1.4)   # 越往下越向外鼓

    thickness = 0.035

    grid_out, grid_in = [], []
    for i in range(n_phi + 1):
        phi = -phi_max + 2 * phi_max * i / n_phi
        col_out, col_in = [], []
        for j in range(n_psi + 1):
            t = j / n_psi
            psi = psi_top + (bot_angle(i) - psi_top) * t
            r_out = outer_radius(t)
            col_out.append(sphere_point(phi, psi, r_out))
            col_in.append(sphere_point(phi, psi, r_out - thickness))
        grid_out.append(col_out)
        grid_in.append(col_in)

    for i in range(n_phi):
        for j in range(n_psi):
            p00, p10 = grid_out[i][j], grid_out[i + 1][j]
            p01, p11 = grid_out[i][j + 1], grid_out[i + 1][j + 1]
            radial = vsub(p00, FACE_CENTER)
            w.add_face([p00, p01, p11, p10], radial)                       # 外壳
            q00, q10 = grid_in[i][j], grid_in[i + 1][j]
            q01, q11 = grid_in[i][j + 1], grid_in[i + 1][j + 1]
            w.add_face([q00, q01, q11, q10],
                       (-radial[0], -radial[1], -radial[2]))               # 内壳
        # 上沿 / 下沿封边（窄条，双面防止漏光）
        w.add_face([grid_out[i][0], grid_out[i + 1][0],
                    grid_in[i + 1][0], grid_in[i][0]], double_sided=True)
        w.add_face([grid_out[i][n_psi], grid_out[i + 1][n_psi],
                    grid_in[i + 1][n_psi], grid_in[i][n_psi]], double_sided=True)

    # 两侧封口
    for i in (0, n_phi):
        for j in range(n_psi):
            w.add_face([grid_out[i][j], grid_out[i][j + 1],
                        grid_in[i][j + 1], grid_in[i][j]], double_sided=True)
    return w

# ---------------- 大八字弯曲胡 ----------------

def build_mustache():
    """大八字弯曲胡：沿球面扫掠的弯管，中间饱满，两翼下垂后末梢向上翘起。
    位置在鼻下唇上（眼睛下方、胡子上沿），横贯脸前。"""
    w = ObjWriter("Mustache_Big8")
    span = 0.30          # 单侧翼展
    n_seg = 22           # 单侧分段数
    n_ring = 8           # 截面边数
    surf_off = 0.012     # 离球面的间隙

    def centerline(u):
        """u ∈ [-1,1]，0 为中线。返回球面外侧的中心点。"""
        s = abs(u)
        x = span * u
        # 八字走势：中段下垂，末梢向上翘
        if s < 0.7:
            y = 0.615 - 0.10 * (s / 0.7) ** 1.5
        else:
            y = 0.615 - 0.10 + 0.14 * ((s - 0.7) / 0.3) ** 2
        dy = y - FACE_CENTER[1]
        z = math.sqrt(max(FACE_RADIUS ** 2 - x * x - dy * dy, 1e-6))
        return (x, y, FACE_CENTER[2] + z + surf_off)

    def half_size(u):
        """截面半宽（贴面方向）与半厚（离面方向），中间粗末梢细。"""
        s = abs(u)
        r = 0.050 - 0.020 * min(s / 0.5, 1.0)          # 0.050 -> 0.030
        if s > 0.7:
            r = 0.030 - 0.018 * (s - 0.7) / 0.3        # -> 0.012
        return r, r * 0.7

    # 单侧扫掠，左右镜像共用（模型本身 X 对称）
    rings = []
    us = [(-1.0 + 2.0 * i / (2 * n_seg)) for i in range(2 * n_seg + 1)]
    for i, u in enumerate(us):
        p = centerline(u)
        # 切线用相邻点差分
        eps = 1e-3
        pa = centerline(max(u - eps, -1.0))
        pb = centerline(min(u + eps, 1.0))
        tangent = vnorm(vsub(pb, pa))
        radial = vnorm(vsub(p, FACE_CENTER))
        side = vnorm(vcross(tangent, radial))          # 贴面横向
        out = vnorm(vcross(side, tangent))             # 离面法向
        hw, hh = half_size(u)
        ring = []
        for k in range(n_ring):
            a = 2 * math.pi * k / n_ring
            ca, sa = math.cos(a), math.sin(a)
            ring.append((p[0] + side[0] * ca * hw + out[0] * sa * hh,
                         p[1] + side[1] * ca * hw + out[1] * sa * hh,
                         p[2] + side[2] * ca * hw + out[2] * sa * hh))
        rings.append(ring)

    for i in range(len(rings) - 1):
        for k in range(n_ring):
            k2 = (k + 1) % n_ring
            quad = [rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]]
            mid = rings[i][k]
            w.add_face(quad, vsub(mid, FACE_CENTER))   # 法线朝球面外
    # 两端封口
    w.add_face(list(rings[0]), vsub(centerline(-1.0), centerline(-0.99)))
    w.add_face(list(rings[-1]), vsub(centerline(1.0), centerline(0.99)))
    return w

# ---------------- 平滑着色网格工具（高面数用） ----------------

def smoothstep(a, b, x):
    t = min(max((x - a) / (b - a), 0.0), 1.0)
    return t * t * (3 - 2 * t)

class SmoothObj:
    """平滑着色网格：共享顶点 + 邻面平均顶点法线，支持整体绕序自动修正。"""
    def __init__(self, name):
        self.name = name
        self.verts = []
        self.faces = []

    def add_ring_surface(self, rows):
        """rows: 等长点环列表（环向闭合），相邻环之间连成四边形带。返回索引环。"""
        idx_rows = []
        for row in rows:
            idx = []
            for p in row:
                self.verts.append(p)
                idx.append(len(self.verts))      # 1-based
            idx_rows.append(idx)
        for i in range(len(rows) - 1):
            a, b = idx_rows[i], idx_rows[i + 1]
            n = len(a)
            for k in range(n):
                k2 = (k + 1) % n
                self.faces.append([a[k], a[k2], b[k2], b[k]])
        return idx_rows

    def add_fan_cap(self, point, ring_idx):
        self.verts.append(point)
        c = len(self.verts)
        n = len(ring_idx)
        for k in range(n):
            self.faces.append([c, ring_idx[k], ring_idx[(k + 1) % n]])

    def orient(self, ref_center, sign=+1):
        """整体绕序修正：让法线总体朝向 sign * 离 ref_center 的方向。"""
        s = 0.0
        for f in self.faces:
            pts = [self.verts[i - 1] for i in f]
            n = face_normal(pts)
            c = (sum(p[0] for p in pts) / len(pts),
                 sum(p[1] for p in pts) / len(pts),
                 sum(p[2] for p in pts) / len(pts))
            s += vdot(n, vsub(c, ref_center)) * sign
        if s < 0:
            self.faces = [list(reversed(f)) for f in self.faces]

    def write_to(self, fh, v_offset):
        fh.write("o %s\n" % self.name)
        for v in self.verts:
            fh.write("v %.6f %.6f %.6f\n" % v)
        # 邻面平均法线
        acc = [[0.0, 0.0, 0.0] for _ in self.verts]
        for f in self.faces:
            n = face_normal([self.verts[i - 1] for i in f])
            for i in f:
                a = acc[i - 1]
                a[0] += n[0]; a[1] += n[1]; a[2] += n[2]
        for a in acc:
            fh.write("vn %.4f %.4f %.4f\n" % vnorm(tuple(a)))
        n_faces = 0
        for f in self.faces:
            fh.write("f " + " ".join("%d//%d" % (i + v_offset, i + v_offset)
                                     for i in f) + "\n")
            n_faces += 1
        return len(self.verts), n_faces

def write_parts(path, parts):
    total_v, total_f = 0, 0
    with open(path, "w", encoding="utf-8") as fh:
        fh.write("# FaceSlapper procedural accessory (smooth, hi-poly)\n")
        fh.write("# Local space: fingers +Z, palm -Y, thumb -X; attach to HandSocket\n")
        for part in parts:
            nv, nf = part.write_to(fh, total_v)
            total_v += nv
            total_f += nf
    print("  %-22s %5d verts, %4d faces -> %s"
          % (os.path.splitext(os.path.basename(path))[0], total_v, total_f, path))

# ---------------- 高面数拳套 ----------------

def _lathe(profile, n):
    """profile: [(r, z), ...] 绕 Z 轴车削出点环列表。"""
    return [[(r * math.cos(2 * math.pi * k / n),
              r * math.sin(2 * math.pi * k / n), z)
             for k in range(n)] for r, z in profile]

def build_boxing_glove():
    """高面数拳套：变形 UV 球拳身（64x44）+ 贝塞尔弯管拇指（40x24）+
    双层车削袖口（64 段），平滑着色，约 4400 面。
    本地空间：手指朝 +Z，掌心朝 -Y，拇指在 -X（右手），原点在腕心。"""
    # ---- 拳身：变形 UV 球 ----
    body = SmoothObj("GloveBody")
    nu, nv = 64, 44
    rx, ry, rz = 0.165, 0.175, 0.215
    rows = []
    for j in range(1, nv):
        v = math.pi * j / nv           # 0=腕后极, pi=拳峰前极
        row = []
        for k in range(nu):
            u = 2 * math.pi * k / nu
            sx, sy, sz = (math.sin(v) * math.cos(u),
                          math.sin(v) * math.sin(u), math.cos(v))
            x, y, z = rx * sx, ry * sy, rz * sz
            z *= 1 - 0.14 * smoothstep(0.55, 1.0, sz)   # 拳峰压平
            if sy < 0:
                y *= 0.93                                # 掌心侧略平
            x *= 1 + 0.06 * math.sin(v) ** 2             # 拳身中段饱满
            row.append((x, y, z))
        rows.append(row)
    idx = body.add_ring_surface(rows)
    body.add_fan_cap((0, 0, -rz), idx[0])                # 腕后极封口
    front_z = rz * (1 - 0.14)
    body.add_fan_cap((0, 0, front_z), idx[-1])           # 拳峰封口
    body.orient((0, 0, 0.02))

    # ---- 拇指：贝塞尔弯管，尖端封口 ----
    thumb = SmoothObj("GloveThumb")
    p0, p1, p2 = (-0.135, -0.02, -0.03), (-0.185, -0.075, 0.10), (-0.065, -0.105, 0.165)
    nt, nr = 41, 24
    path = []
    for i in range(nt):
        t = i / (nt - 1)
        mt = 1 - t
        path.append(tuple(mt * mt * p0[d] + 2 * mt * t * p1[d] + t * t * p2[d]
                          for d in range(3)))
    trows = []
    for i, p in enumerate(path):
        t = i / (nt - 1)
        pa, pb = path[max(i - 1, 0)], path[min(i + 1, nt - 1)]
        tangent = vnorm(vsub(pb, pa))
        side = vcross(tangent, (0, 1, 0))
        if vdot(side, side) < 1e-6:
            side = vcross(tangent, (1, 0, 0))
        side = vnorm(side)
        upv = vnorm(vcross(side, tangent))
        r = 0.058 - 0.022 * t
        trows.append([(p[0] + side[0] * math.cos(2 * math.pi * k / nr) * r
                       + upv[0] * math.sin(2 * math.pi * k / nr) * r * 0.9,
                       p[1] + side[1] * math.cos(2 * math.pi * k / nr) * r
                       + upv[1] * math.sin(2 * math.pi * k / nr) * r * 0.9,
                       p[2] + side[2] * math.cos(2 * math.pi * k / nr) * r
                       + upv[2] * math.sin(2 * math.pi * k / nr) * r * 0.9)
                      for k in range(nr)])
    tidx = thumb.add_ring_surface(trows)
    tip_t = vnorm(vsub(path[-1], path[-2]))
    thumb.add_fan_cap(tuple(path[-1][d] + tip_t[d] * 0.025 for d in range(3)), tidx[-1])
    tc = tuple(sum(p[d] for p in path) / len(path) for d in range(3))
    thumb.orient(tc)

    # ---- 袖口：外层翻边 + 内层套筒 ----
    nlat = 64
    cuff_out = SmoothObj("GloveCuffOuter")
    cuff_out.add_ring_surface(_lathe([
        (0.150, -0.10), (0.152, -0.16), (0.158, -0.21),
        (0.172, -0.26), (0.178, -0.295), (0.172, -0.315), (0.150, -0.322),
    ], nlat))
    cuff_out.orient((0, 0, -0.21))

    cuff_in = SmoothObj("GloveCuffInner")
    cuff_in.add_ring_surface(_lathe([
        (0.150, -0.322), (0.138, -0.26), (0.130, -0.16), (0.126, -0.10),
    ], nlat))
    cuff_in.orient((0, 0, -0.21), sign=-1)               # 法线朝轴心

    return [body, thumb, cuff_out, cuff_in]

# ---------------- 眼镜 ----------------

def outline_circle(n=20):
    return [(math.cos(2 * math.pi * k / n), math.sin(2 * math.pi * k / n))
            for k in range(n)]

def outline_square():
    return [(-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0)]

def outline_star(points=5, inner=0.45):
    pts = []
    for k in range(points * 2):
        r = 1.0 if k % 2 == 0 else inner
        a = math.pi / 2 + math.pi * k / points   # 从顶部尖角开始
        pts.append((r * math.cos(a), r * math.sin(a)))
    return pts

def outline_heart(n=24):
    pts = []
    for k in range(n):
        t = 2 * math.pi * k / n
        x = 16 * math.sin(t) ** 3 / 17.0
        y = (13 * math.cos(t) - 5 * math.cos(2 * t)
             - 2 * math.cos(3 * t) - math.cos(4 * t)) / 17.0
        pts.append((x, y - 0.06))   # 稍下移，让心尖垂在镜框下缘
    return pts

def add_rim(w, outline, center, yaw_deg, half_w, half_h, inner_scale=0.72,
            depth=0.020):
    """在 center 处放一个指定轮廓的镜框圈（面片环 + 前后挤出，带内外壁）。"""
    yaw = math.radians(yaw_deg)
    right = (math.cos(yaw), 0, -math.sin(yaw))
    fwd = (math.sin(yaw), 0, math.cos(yaw))    # 镜框朝向（随球面外翻）
    up = (0, 1, 0)
    zf, zb = depth / 2, -depth / 2

    def to_world(x, y, z):
        return (center[0] + right[0] * x + up[0] * y + fwd[0] * z,
                center[1] + right[1] * x + up[1] * y + fwd[1] * z,
                center[2] + right[2] * x + up[2] * y + fwd[2] * z)

    n = len(outline)
    outer = [(x * half_w, y * half_h) for x, y in outline]
    inner = [(x * half_w * inner_scale, y * half_h * inner_scale) for x, y in outline]

    for k in range(n):
        k2 = (k + 1) % n
        of0, of1 = to_world(*outer[k], zf), to_world(*outer[k2], zf)
        if0, if1 = to_world(*inner[k], zf), to_world(*inner[k2], zf)
        ob0, ob1 = to_world(*outer[k], zb), to_world(*outer[k2], zb)
        ib0, ib1 = to_world(*inner[k], zb), to_world(*inner[k2], zb)

        w.add_face([of0, of1, if1, if0], fwd)                 # 前面环
        w.add_face([ob0, ob1, ib1, ib0],
                   (-fwd[0], -fwd[1], -fwd[2]))               # 背面环
        radial = vnorm((outer[k][0] + outer[k2][0],
                        outer[k][1] + outer[k2][1], 0))
        out_dir = (right[0] * radial[0] + up[0] * radial[1],
                   right[1] * radial[0] + up[1] * radial[1],
                   right[2] * radial[0] + up[2] * radial[1])
        w.add_face([of0, of1, ob1, ob0], out_dir)             # 外壁
        w.add_face([if0, if1, ib1, ib0],
                   (-out_dir[0], -out_dir[1], -out_dir[2]))   # 内壁

def build_glasses(name, outline, half_w, half_h):
    """双镜框 + 鼻梁 + 两条镜腿，整体贴合球脸。"""
    w = ObjWriter(name)
    eye_x, rim_y = 0.13, 0.71
    # 球面在 (eye_x, rim_y) 处的外法线，用于决定镜框外倾角与前移量
    dz = math.sqrt(FACE_RADIUS ** 2 - eye_x ** 2 - (rim_y - FACE_CENTER[1]) ** 2)
    yaw = math.degrees(math.atan2(eye_x, dz))
    rim_z = FACE_CENTER[2] + dz + 0.032

    for side in (+1, -1):
        add_rim(w, outline, (side * eye_x, rim_y, rim_z),
                yaw * side, half_w, half_h)

    # 鼻梁（两个镜框之间的短横杆，略高于镜框中心）
    w.add_box((0, rim_y + 0.018, rim_z + 0.004), (0.085, 0.026, 0.020))

    # 镜腿：从镜框外缘贴着头侧向后延伸
    leg_start = (eye_x + half_w * 0.95, rim_y, rim_z - 0.005)
    leg_end = (0.555, rim_y - 0.02, 0.10)
    for side in (+1, -1):
        a = (side * leg_start[0], leg_start[1], leg_start[2])
        b = (side * leg_end[0], leg_end[1], leg_end[2])
        d = vsub(b, a)
        length = math.sqrt(vdot(d, d))
        fwd = vnorm(d)
        right = vnorm(vcross((0, 1, 0), fwd))
        up = vcross(fwd, right)
        mid = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (a[2] + b[2]) / 2)
        w.add_box(mid, (0.024, 0.030, length), right, up, fwd)
    return w

# ---------------- 入口 ----------------

def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    print("生成脸部配饰 OBJ -> %s" % OUT_DIR)

    build_beard().write(os.path.join(OUT_DIR, "Beard.obj"))
    build_mustache().write(os.path.join(OUT_DIR, "Mustache_Big8.obj"))
    write_parts(os.path.join(OUT_DIR, "BoxingGlove_HiPoly.obj"), build_boxing_glove())
    build_glasses("Glasses_Circle", outline_circle(), 0.088, 0.088) \
        .write(os.path.join(OUT_DIR, "Glasses_Circle.obj"))
    build_glasses("Glasses_Square", outline_square(), 0.088, 0.082) \
        .write(os.path.join(OUT_DIR, "Glasses_Square.obj"))
    build_glasses("Glasses_Star", outline_star(), 0.098, 0.098) \
        .write(os.path.join(OUT_DIR, "Glasses_Star.obj"))
    build_glasses("Glasses_Heart", outline_heart(), 0.096, 0.088) \
        .write(os.path.join(OUT_DIR, "Glasses_Heart.obj"))

if __name__ == "__main__":
    main()
