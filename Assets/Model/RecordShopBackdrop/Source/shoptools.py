"""ローポリのモデルを作る小さな道具（build_mascots.py と同じもの）。"""
import math
import os

import numpy as np

from lathe import Mesh, fix_winding, write_obj, tri_count, lathe
from shapes import box, cylinder, disc_profile, transformed, merge
from deco import tube

MAT = {}
stats = {}
OUT = 'out'

# ------------------------------------------------------------------ 道具
def rot_y_to(d):
    """Y 軸を向き d に回す回転行列。"""
    d = np.asarray(d, float)
    d = d / np.linalg.norm(d)
    y = np.array([0.0, 1.0, 0.0])
    v = np.cross(y, d)
    c = float(np.dot(y, d))
    if np.linalg.norm(v) < 1e-9:
        return np.eye(3) if c > 0 else np.diag([1.0, -1.0, -1.0])
    vx = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    return np.eye(3) + vx + vx @ vx * (1 / (1 + c))


def rot_euler(rx=0.0, ry=0.0, rz=0.0):
    ax, ay, az = (math.radians(a) for a in (rx, ry, rz))
    Rx = np.array([[1, 0, 0], [0, math.cos(ax), -math.sin(ax)], [0, math.sin(ax), math.cos(ax)]])
    Ry = np.array([[math.cos(ay), 0, math.sin(ay)], [0, 1, 0], [-math.sin(ay), 0, math.cos(ay)]])
    Rz = np.array([[math.cos(az), -math.sin(az), 0], [math.sin(az), math.cos(az), 0], [0, 0, 1]])
    return Ry @ Rx @ Rz


def put(m, part, R=None, t=(0, 0, 0)):
    R = np.eye(3) if R is None else np.asarray(R, float)
    merge(m, transformed(part, R.tolist(), tuple(map(float, t))))


def BOX(m, c, size, mat, rx=0.0, ry=0.0, rz=0.0):
    t = Mesh()
    w, h, d = size
    box(t, (-w / 2, -h / 2, -d / 2), (w / 2, h / 2, d / 2), mat)
    put(m, t, rot_euler(rx, ry, rz), c)


def ELLIPSOID(m, c, r, mat, seg=14, rings=7, R=None):
    """楕円体（球を伸ばしたもの）。r = (rx, ry, rz)。"""
    t = Mesh()
    prof = []
    steps = [math.pi / 2 - math.pi * i / rings for i in range(rings + 1)]
    for a0, a1 in zip(steps[:-1], steps[1:]):
        prof.append(((math.cos(a0), math.sin(a0)), (math.cos(a1), math.sin(a1)), mat, 1.0))
    lathe(t, prof, seg, 'planar')
    S = np.diag(r)
    put(m, t, (np.eye(3) if R is None else np.asarray(R)) @ S, c)


def ROD(m, p0, p1, r, mat, seg=8, r1=None):
    """p0 から p1 への円柱（r1 を変えると先が細くなる）。"""
    p0, p1 = np.asarray(p0, float), np.asarray(p1, float)
    L = float(np.linalg.norm(p1 - p0))
    r1 = r if r1 is None else r1
    t = Mesh()
    prof = [((0.0, L), (r1, L), mat, r), ((r1, L), (r, 0.0), mat, r), ((r, 0.0), (0.0, 0.0), mat, r)]
    prof = [p for p in prof if p[0] != p[1]]
    cylinder(t, prof, seg, (0, 0, 0))
    put(m, t, rot_y_to(p1 - p0), p0)


def DISC(m, c, r, thick, mat, axis=(0, 0, -1), seg=20, top=None):
    """平たい円柱（axis の向きに面が向く）。"""
    t = Mesh()
    cylinder(t, disc_profile(r, -thick / 2, thick / 2, top or mat, mat, mat), seg, (0, 0, 0))
    put(m, t, rot_y_to(axis), c)


def ARC(m, center, radius, a0, a1, r, mat, plane='xy', n=16, sides=8):
    """円の一部をパイプでなぞる（ヘッドホンのバンドなど）。角度は度。"""
    c = np.asarray(center, float)
    pts, ups = [], []
    for i in range(n + 1):
        a = math.radians(a0 + (a1 - a0) * i / n)
        if plane == 'xy':
            p = c + radius * np.array([math.cos(a), math.sin(a), 0.0])
            up = (0.0, 0.0, -1.0)
        else:  # xz（水平の輪）
            p = c + radius * np.array([math.cos(a), 0.0, math.sin(a)])
            up = (0.0, 1.0, 0.0)
        pts.append(p)
        ups.append(up)
    tube(m, pts, r, mat, ups, sides=sides)


def PATH(m, pts, r, mat, sides=6):
    pts = np.asarray(pts, float)
    tube(m, pts, r, mat, [(0.0, 0.0, -1.0)] * len(pts), sides=sides)


def front_z(c, r, x, y, lift=0.004):
    """楕円体（中心 c・半径 r）の正面の表面の z（少し手前に浮かせる）。"""
    u = ((x - c[0]) / r[0]) ** 2 + ((y - c[1]) / r[1]) ** 2
    return c[2] - r[2] * math.sqrt(max(0.0, 1 - u)) - lift


def FACE_ARC(m, c, r, pts2d, rad, mat):
    """顔の表面に沿った線（目・口など）。pts2d は (x, y) の列。"""
    PATH(m, [(x, y, front_z(c, r, x, y)) for x, y in pts2d], rad, mat)


def save(m, name):
    fix_winding(m)
    write_obj(m, os.path.join(OUT, f'{name}.obj'), name, {k: MAT[k] for k in m.order})
    stats[name] = tri_count(m)


def arc2d(cx, cy, rx, ry, a0, a1, n=8):
    return [(cx + rx * math.cos(math.radians(a0 + (a1 - a0) * i / n)),
             cy + ry * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]


