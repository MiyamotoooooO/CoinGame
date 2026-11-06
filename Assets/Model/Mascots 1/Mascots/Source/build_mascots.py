"""マスコットキャラ4案（ヴィニー・コバン・メダリオ・ネオン）の3Dモデルを作る（ローポリ・単色・OBJ/MTL）。

単位はメートル、Y が上、正面は -Z。原点は足もと。高さはどれも約1m（Unity の Scale で大きさを合わせる）。
使い方: python build_mascots.py 出力先フォルダ
"""
import math
import os
import sys

import numpy as np

from lathe import Mesh, fix_winding, write_obj, tri_count, lathe
from shapes import box, cylinder, disc_profile, transformed, merge
from deco import tube

OUT = sys.argv[1] if len(sys.argv) > 1 else 'out'
os.makedirs(OUT, exist_ok=True)

MAT = {
    'Navy': (0.11, 0.08, 0.25, 0.3),
    'Night': (0.16, 0.14, 0.31, 0.3),
    'Black': (0.06, 0.06, 0.08, 0.4),
    'Groove': (0.17, 0.16, 0.22, 0.5),
    'White': (0.96, 0.95, 0.92, 0.3),
    'Cream': (1.00, 0.97, 0.93, 0.3),
    'Orange': (1.00, 0.48, 0.10, 0.4),
    'Pink': (1.00, 0.24, 0.55, 0.4),
    'PinkSoft': (1.00, 0.56, 0.72, 0.2),
    'Cyan': (0.21, 0.84, 1.00, 0.5),
    'Yellow': (1.00, 0.82, 0.25, 0.5),
    'Purple': (0.48, 0.25, 0.89, 0.3),
    'Gold': (1.00, 0.82, 0.25, 0.8),
    'GoldDark': (0.91, 0.64, 0.09, 0.8),
    'GoldLine': (0.95, 0.74, 0.17, 0.8),
    'Skin': (0.97, 0.83, 0.71, 0.2),
    'Hair': (0.23, 0.12, 0.42, 0.3),
    'Mouth': (0.76, 0.20, 0.37, 0.2),
}
stats = {}


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


# ================================================================== A. ヴィニー
m = Mesh()
for s in (-1, 1):
    x = s * 0.075
    BOX(m, (x, 0.045, -0.01), (0.12, 0.05, 0.26), 'Cyan')                       # ローラースケート
    for z in (-0.09, 0.07):
        DISC(m, (x, 0.022, z), 0.025, 0.10, 'Yellow', axis=(1, 0, 0), seg=10)    # 車輪
    ROD(m, (x, 0.07, 0.0), (x, 0.28, 0.0), 0.035, 'Night')                      # 脚
BOX(m, (0, 0.42, 0), (0.42, 0.28, 0.18), 'Night')                               # ラジカセの体
BOX(m, (0, 0.42, 0), (0.44, 0.30, 0.16), 'Yellow')                              # 黄色のふち
for s in (-1, 1):
    DISC(m, (s * 0.105, 0.40, -0.095), 0.075, 0.02, 'Black', seg=16)            # スピーカー
    DISC(m, (s * 0.105, 0.40, -0.105), 0.033, 0.01, 'Pink', seg=12)
BOX(m, (0, 0.515, -0.095), (0.12, 0.04, 0.02), 'Cyan')                          # テープの窓
ROD(m, (-0.21, 0.50, 0), (-0.33, 0.62, -0.02), 0.035, 'Night')                  # 上げた腕
ROD(m, (-0.33, 0.62, -0.02), (-0.36, 0.78, -0.03), 0.035, 'Night')
ELLIPSOID(m, (-0.365, 0.82, -0.03), (0.055, 0.055, 0.055), 'White', seg=10, rings=5)
ROD(m, (0.21, 0.48, 0), (0.30, 0.36, -0.03), 0.035, 'Night')                    # 下げた腕
ROD(m, (0.30, 0.36, -0.03), (0.33, 0.26, -0.05), 0.035, 'Night')
ELLIPSOID(m, (0.335, 0.235, -0.05), (0.055, 0.055, 0.055), 'White', seg=10, rings=5)
ROD(m, (0, 0.56, 0), (0, 0.60, 0), 0.04, 'Black')                               # 首
HC = (0, 0.84, 0)                                                               # 頭（レコード）
DISC(m, HC, 0.27, 0.035, 'Cyan', seg=32)
DISC(m, HC, 0.262, 0.045, 'Black', seg=32)
for rr, mat in ((0.235, 'Groove'), (0.228, 'Black'), (0.205, 'Groove'), (0.198, 'Black'), (0.175, 'Groove'), (0.168, 'Black')):
    DISC(m, HC, rr, 0.046 + (0.001 if mat == 'Groove' else 0.002) * (1 if rr > 0.2 else 2), mat, seg=32)
DISC(m, HC, 0.115, 0.056, 'Orange', seg=24)                                     # ラベル（顔）
zf = HC[2] - 0.028
BOX(m, (-0.045, 0.865, zf - 0.006), (0.085, 0.038, 0.012), 'Navy', rz=-6)      # サングラス
BOX(m, (0.045, 0.865, zf - 0.006), (0.085, 0.038, 0.012), 'Navy', rz=6)
BOX(m, (-0.045, 0.872, zf - 0.013), (0.05, 0.006, 0.004), 'Pink')
BOX(m, (0.045, 0.872, zf - 0.013), (0.05, 0.006, 0.004), 'Cyan')
PATH(m, [(x, y, zf - 0.006) for x, y in arc2d(0, 0.81, 0.04, 0.022, 200, 340)], 0.007, 'Navy')  # 口
ARC(m, HC, 0.30, 15, 165, 0.022, 'Pink')                                        # ヘッドホン
for s in (-1, 1):
    BOX(m, (s * 0.29, 0.84, 0), (0.07, 0.15, 0.10), 'Yellow')
save(m, 'Mascot_Vinny')

# ================================================================== B. コバン
m = Mesh()
PATH(m, [(0.16, 0.18, 0.15), (0.26, 0.24, 0.20), (0.30, 0.36, 0.18), (0.27, 0.48, 0.14)], 0.04, 'Cream', sides=8)  # しっぽ
BC, BR = (0, 0.29, 0), (0.24, 0.26, 0.20)
ELLIPSOID(m, BC, BR, 'Cream')                                                    # 体
ELLIPSOID(m, (0, 0.24, 0), (0.25, 0.20, 0.21), 'Purple')                         # スタジャン
ARC(m, (0, 0.40, 0), 0.20, 0, 360, 0.012, 'Yellow', plane='xz', n=24)           # スタジャンのふち
for s in (-1, 1):
    ELLIPSOID(m, (s * 0.11, 0.045, -0.09), (0.08, 0.045, 0.10), 'Cream', seg=10, rings=5)  # 足
HC, HR = (0, 0.68, 0), (0.24, 0.21, 0.21)
for s in (-1, 1):                                                                # 耳
    ROD(m, (s * 0.14, 0.80, 0.0), (s * 0.20, 0.96, -0.01), 0.075, 'Cream', seg=6, r1=0.002)
    ROD(m, (s * 0.145, 0.81, -0.03), (s * 0.195, 0.93, -0.04), 0.045, 'PinkSoft', seg=6, r1=0.002)
ELLIPSOID(m, HC, HR, 'Cream')                                                    # 頭
ELLIPSOID(m, (-0.10, 0.80, -0.06), (0.09, 0.07, 0.10), 'Orange', seg=10, rings=5)   # ぶち
ELLIPSOID(m, (0.17, 0.70, -0.08), (0.06, 0.07, 0.08), 'Black', seg=10, rings=5)
for s in (-1, 1):                                                                # おでこのサングラス
    x = s * 0.06
    BOX(m, (x, 0.80, front_z(HC, HR, x, 0.80) + 0.004), (0.085, 0.04, 0.02), 'Cyan', rx=-25)
BOX(m, (0, 0.80, front_z(HC, HR, 0, 0.80) - 0.002), (0.04, 0.01, 0.01), 'Navy', rx=-25)
for s in (-1, 1):                                                                # にっこりの目
    FACE_ARC(m, HC, HR, arc2d(s * 0.08, 0.66, 0.04, 0.03, 20, 160), 0.008, 'Navy')
    ELLIPSOID(m, (s * 0.135, 0.615, front_z(HC, HR, s * 0.135, 0.615) + 0.012), (0.035, 0.02, 0.01), 'PinkSoft', seg=8, rings=4)
    for dy in (-0.01, 0.025):                                                     # ひげ
        PATH(m, [(s * 0.12, 0.60 + dy * 0.3, front_z(HC, HR, s * 0.12, 0.60)), (s * 0.24, 0.60 + dy, front_z(HC, HR, s * 0.12, 0.60) + 0.03)], 0.004, 'Navy', sides=4)
ELLIPSOID(m, (0, 0.615, front_z(HC, HR, 0, 0.615) + 0.006), (0.022, 0.014, 0.012), 'Pink', seg=8, rings=4)  # 鼻
FACE_ARC(m, HC, HR, arc2d(-0.022, 0.592, 0.022, 0.016, 200, 340) + arc2d(0.022, 0.592, 0.022, 0.016, 200, 340), 0.006, 'Navy')  # ω
ARC(m, (0, 0.475, -0.01), 0.165, 0, 360, 0.02, 'Pink', plane='xz', n=24)       # 首輪
ELLIPSOID(m, (0, 0.455, -0.185), (0.03, 0.03, 0.03), 'Gold', seg=10, rings=5)   # 鈴
ROD(m, (0.17, 0.40, -0.06), (0.26, 0.60, -0.07), 0.06, 'Cream', seg=10)         # 手まねきの腕
ELLIPSOID(m, (0.27, 0.65, -0.07), (0.075, 0.075, 0.07), 'Cream', seg=10, rings=5)
for dx in (-0.025, 0.0, 0.025):
    ELLIPSOID(m, (0.27 + dx, 0.70, -0.11), (0.012, 0.012, 0.008), 'PinkSoft', seg=6, rings=3)
MC = (-0.09, 0.30, -0.215)                                                       # 抱えたメダル
DISC(m, MC, 0.10, 0.025, 'GoldDark', seg=24)
DISC(m, MC, 0.09, 0.027, 'Gold', seg=24)
DISC(m, MC, 0.065, 0.028, 'GoldLine', seg=20)
DISC(m, MC, 0.06, 0.029, 'Gold', seg=20)
DISC(m, MC, 0.025, 0.031, 'Orange', seg=12)
ELLIPSOID(m, (-0.18, 0.24, -0.21), (0.06, 0.055, 0.055), 'Cream', seg=10, rings=5)  # メダルを持つ手
save(m, 'Mascot_Koban')

# ================================================================== C. メダリオ
m = Mesh()
for s in (-1, 1):
    ROD(m, (s * 0.10, 0.24, 0), (s * 0.13, 0.08, -0.01), 0.022, 'Black')       # 脚
    BOX(m, (s * 0.14, 0.045, -0.04), (0.11, 0.07, 0.20), 'Cyan')                # スニーカー
    BOX(m, (s * 0.14, 0.05, -0.04), (0.112, 0.016, 0.202), 'Pink')
    BOX(m, (s * 0.14, 0.013, -0.04), (0.115, 0.026, 0.205), 'White')
CC = (0, 0.56, 0)
DISC(m, CC, 0.34, 0.08, 'GoldDark', seg=36)                                     # メダルの体
DISC(m, CC, 0.30, 0.085, 'Gold', seg=36)
for rr, mat in ((0.25, 'GoldLine'), (0.243, 'Gold'), (0.21, 'GoldLine'), (0.203, 'Gold')):
    DISC(m, CC, rr, 0.086 + (0.001 if mat == 'GoldLine' else 0.002), mat, seg=32)
ARC(m, CC, 0.345, 30, 150, 0.05, 'Pink', n=20, sides=10)                        # 汗止めバンド
zf = -0.045
for s in (-1, 1):                                                                # サングラス
    BOX(m, (s * 0.11, 0.59, zf - 0.006), (0.17, 0.09, 0.012), 'Black')
BOX(m, (0, 0.61, zf - 0.006), (0.06, 0.02, 0.012), 'Black')
BOX(m, (-0.11, 0.615, zf - 0.013), (0.10, 0.008, 0.004), 'Pink')
BOX(m, (0.11, 0.615, zf - 0.013), (0.10, 0.008, 0.004), 'Cyan')
t = Mesh()                                                                       # にかっと笑う口（半円）
DISC(t, (0, 0, 0), 0.12, 0.012, 'Black', seg=20)
put(m, t, np.diag([1.0, 0.45, 1.0]), (0, 0.44, zf - 0.006))
BOX(m, (0, 0.465, zf - 0.013), (0.20, 0.022, 0.004), 'White')
for s in (-1, 1):                                                                # 腕と手袋
    end = (s * 0.50, 0.78, -0.02) if s < 0 else (s * 0.48, 0.40, -0.03)
    ROD(m, (s * 0.33, 0.60 if s < 0 else 0.52, 0), end, 0.022, 'Black')
    ELLIPSOID(m, end, (0.055, 0.055, 0.05), 'White', seg=10, rings=5)
save(m, 'Mascot_Medario')

# ================================================================== D. ネオン
m = Mesh()
for s in (-1, 1):
    x = s * 0.065
    BOX(m, (x, 0.035, -0.02), (0.09, 0.06, 0.16), 'Cyan')                      # 靴
    ROD(m, (x, 0.06, 0), (x, 0.32, 0), 0.03, 'Skin')                           # 脚
    ROD(m, (x, 0.07, 0), (x, 0.20, 0), 0.045, 'Pink', seg=10)                  # レッグウォーマー
    ARC(m, (x, 0.12, 0), 0.046, 0, 360, 0.006, 'Yellow', plane='xz', n=12, sides=4)
t = Mesh()                                                                       # スカート＋ジャケット（下が広い）
cylinder(t, [((0.0, 0.30), (0.15, 0.30), 'Purple', 0.2), ((0.15, 0.30), (0.22, 0.0), 'Purple', 0.2), ((0.22, 0.0), (0.0, 0.0), 'Purple', 0.2)], 16, (0, 0, 0))
put(m, t, np.diag([1.0, 1.0, 0.8]), (0, 0.28, 0))
BOX(m, (0, 0.52, -0.115), (0.08, 0.10, 0.01), 'Yellow', rx=-12)                 # 中のシャツ
for s in (-1, 1):
    BOX(m, (s * 0.15, 0.565, 0), (0.12, 0.07, 0.17), 'Pink', rz=-s * 12)         # 肩パッド
    ROD(m, (s * 0.17, 0.54, 0), (s * 0.15, 0.42, -0.10), 0.035, 'Purple')      # 腕
    ELLIPSOID(m, (s * 0.13, 0.40, -0.15), (0.035, 0.035, 0.035), 'Skin', seg=8, rings=4)  # 手
DISC(m, (0.06, 0.52, -0.175), 0.11, 0.01, 'Black', seg=24)                      # ジャケットからのぞくレコード
DISC(m, (0.06, 0.52, -0.181), 0.04, 0.002, 'Orange', seg=12)
BOX(m, (0, 0.42, -0.185), (0.25, 0.25, 0.012), 'Cyan', rz=-8)                   # ジャケット（レコードの袋）
BOX(m, (-0.01, 0.43, -0.193), (0.14, 0.012, 0.004), 'Navy', rz=-8)
BOX(m, (-0.01, 0.39, -0.193), (0.11, 0.012, 0.004), 'Navy', rz=-8)
ROD(m, (0, 0.58, 0), (0, 0.63, 0), 0.035, 'Skin')                               # 首
ELLIPSOID(m, (0, 0.82, 0.05), (0.23, 0.23, 0.19), 'Hair')                       # ふくらんだ髪（後ろ）
HC, HR = (0, 0.77, 0), (0.165, 0.16, 0.155)
ELLIPSOID(m, HC, HR, 'Skin')                                                    # 顔
ELLIPSOID(m, (0, 0.89, -0.09), (0.17, 0.07, 0.09), 'Hair', seg=12, rings=6)     # 前髪
ELLIPSOID(m, (0.235, 0.78, 0.05), (0.06, 0.13, 0.06), 'Hair', seg=10, rings=5, R=rot_euler(0, 0, -20))  # サイドテール
ELLIPSOID(m, (0.21, 0.88, 0.05), (0.035, 0.03, 0.035), 'Yellow', seg=8, rings=4)  # シュシュ
for s in (-1, 1):
    x = s * 0.058
    ELLIPSOID(m, (x, 0.765, front_z(HC, HR, x, 0.765) + 0.004), (0.024, 0.034, 0.01), 'Navy', seg=10, rings=5)  # 目
    ELLIPSOID(m, (x + 0.008, 0.778, front_z(HC, HR, x, 0.778) - 0.004), (0.008, 0.008, 0.005), 'White', seg=6, rings=3)
    ELLIPSOID(m, (s * 0.10, 0.725, front_z(HC, HR, s * 0.10, 0.725) + 0.006), (0.028, 0.015, 0.008), 'PinkSoft', seg=8, rings=4)
FACE_ARC(m, HC, HR, arc2d(0, 0.715, 0.022, 0.014, 200, 340, 6), 0.006, 'Mouth')  # 口
ARC(m, (0, 0.80, 0), 0.215, 10, 170, 0.016, 'Cyan')                             # ヘッドホン
for s in (-1, 1):
    DISC(m, (s * 0.20, 0.78, 0), 0.05, 0.05, 'Orange', axis=(s, 0, 0), seg=14)
save(m, 'Mascot_Neon')

for k, v in stats.items():
    print(f'{k}: {v} tri')
