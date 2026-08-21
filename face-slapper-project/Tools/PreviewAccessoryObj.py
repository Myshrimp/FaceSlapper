# -*- coding: utf-8 -*-
"""读取 Accessories 目录下的 OBJ，画正视图/侧视图 2D 投影，用于人工检查形状。
正视图：从脸前方 +Z 看（X 横轴，Y 纵轴）；侧视图：从右侧 +X 看（Z 横轴，Y 纵轴）。
"""
import os

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Polygon, Circle

DIR = os.path.join("Assets", "Art", "Meshes", "Accessories")
FILES = ["Beard.obj", "Mustache_Big8.obj", "BoxingGlove_HiPoly.obj",
         "Glasses_Circle.obj", "Glasses_Square.obj", "Glasses_Star.obj",
         "Glasses_Heart.obj"]

def load_obj(path):
    vs, faces = [], []
    with open(path, encoding="utf-8") as f:
        for line in f:
            if line.startswith("v "):
                vs.append(tuple(float(x) for x in line.split()[1:4]))
            elif line.startswith("f "):
                faces.append([int(t.split("//")[0]) - 1 for t in line.split()[1:]])
    return vs, faces

def draw(ax, vs, faces, facecolor, haxis, depth, with_face=True):
    """haxis: 横轴坐标分量(0=x,2=z)；depth: 深度分量，小的先画。"""
    polys = []
    for face in faces:
        pts = [(vs[i][haxis], vs[i][1]) for i in face]
        d = sum(vs[i][depth] for i in face) / len(face)
        polys.append((d, pts))
    polys.sort(key=lambda p: p[0])
    for _, pts in polys:
        ax.add_patch(Polygon(pts, closed=True, facecolor=facecolor,
                             edgecolor="black", linewidth=0.3))
    if with_face:
        # 脸球参考：圆 + 眼睛位置
        ax.add_patch(Circle((0, 0.6), 0.6, fill=False, edgecolor="gray",
                            linestyle="--", linewidth=1))
        ax.plot([-0.13, 0.13] if haxis == 0 else [0.575, 0.575],
                [0.72, 0.72], "rs", markersize=5)

fig, axes = plt.subplots(2, len(FILES), figsize=(20, 8))
for col, fname in enumerate(FILES):
    vs, faces = load_obj(os.path.join(DIR, fname))
    color = (0.35, 0.20, 0.08) if "Beard" in fname else \
            (0.55, 0.30, 0.10) if "Mustache" in fname else \
            (0.75, 0.12, 0.12) if "BoxingGlove" in fname else (0.10, 0.10, 0.12)
    glove = "BoxingGlove" in fname     # 拳套在武器本地空间（原点），无脸球参考
    for row, (haxis, depth, title) in enumerate(((0, 2, "front (+Z)"),
                                                 (2, 0, "side (+X)"))):
        ax = axes[row][col]
        draw(ax, vs, faces, color, haxis, depth, with_face=not glove)
        ax.set_title(f"{fname}\n{title}", fontsize=9)
        if glove:
            ax.set_xlim(-0.38, 0.38)
            ax.set_ylim(-0.38, 0.38)
        else:
            ax.set_xlim(-0.75, 0.75)
            ax.set_ylim(-0.15, 1.35)
        ax.set_aspect("equal")
        ax.grid(alpha=0.3)
plt.tight_layout()
out = os.path.join("Tools", "accessory_preview.png")
plt.savefig(out, dpi=90)
print("saved:", out)
