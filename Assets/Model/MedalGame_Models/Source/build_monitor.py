"""モニターの枠（MonitorFrame.obj）を作る：ZZZ風の角ばったケース（v5）。

WITH_CASE = False（いまの設定）なら、まわりのグレーのケースと飾りは作らず、画面のまわりの黒い縁だけを作る。
True にすると、ケース（角を切り落とした板・オレンジの線・ランプ・通気口・ねじ）も作る。

画面の穴の大きさ・位置（400×160mm、中心の高さ 190mm、前面 z=196mm）と、
枠の外形の上端（284mm：ライトバーの位置）は前と同じ。
マテリアルの並び（Trim / Bezel / MonitorBack）も前と同じにして、シーンでの割り当てがずれないようにしている。

使い方: python build_monitor.py 出力先フォルダ
"""
import math
import os
import sys

import numpy as np

from lathe import Mesh, fix_winding, write_obj, tri_count
from shapes import box
from deco import ring_face, extrude_outline, rounded_rect, _add

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)


def m(*v):
    """mm → m"""
    return tuple(x / 1000 for x in v) if len(v) > 1 else v[0] / 1000


# ---------------------------------------------------------------- 寸法（mm）
MON_W, MON_H = 400, 160          # 画面（前と同じ）
MON_CY = 190                     # 画面の中心の高さ（前と同じ）
Z0, Z1 = 196, 212                # 枠の前面と背面（前と同じ）
LIP = 9                          # 画面のまわりの黒い縁の太さ
LIP_OUT = 3                      # 黒い縁が前に出る量
CASE_HW = 236                    # ケースの横幅の半分
CASE_TOP = 284                   # ケースの上端（前と同じ）
CASE_BOTTOM = 88                 # ケースの下端（前と同じ）
CUT_BIG = 30                     # 大きく切り落とす角（プレイヤーから見て左上・右下）
CUT_SMALL = 8                    # 小さく切り落とす角
DECO_OUT = 1.2                   # 飾りが前に出る量
WITH_CASE = False                # まわりのグレーのケースと飾りを作るか

# 色：(r, g, b, つや)
MAT = {
    'Trim': (0.035, 0.035, 0.040, 0.85),        # 画面のまわりの黒い縁（つやあり）
    'Bezel': (0.115, 0.115, 0.130, 0.35),       # ケース（黒に近いグレー）
    'MonitorBack': (0.02, 0.02, 0.03, 0.2),
    'Accent': (1.000, 0.478, 0.102, 0.5),       # 差し色のオレンジ（モニターのUIと同じ #FF7A1A）
    'Label': (0.950, 0.950, 0.930, 0.4),        # 白いタグ
    'Vent': (0.020, 0.020, 0.025, 0.1),         # 通気口のすき間
    'Screw': (0.550, 0.550, 0.580, 0.9),        # ねじ
}

# プレイヤーから見た左右：OBJ では、プレイヤーの右手側が -X（Unity に取り込むと反転して +X）
LEFT, RIGHT = 1, -1

mesh = Mesh()
c2 = np.array(m(0, MON_CY))
hw, hh = MON_W / 2, MON_H / 2


def chamfer_rect(x0, x1, y0, y1, cuts):
    """角を斜めに切り落とした長方形。cuts = (右上, 左上, 左下, 右下) の切り落とし量（OBJ の +X が左）。"""
    tr, tl, bl, br = cuts   # OBJ 座標で (+X+Y), (-X+Y), (-X-Y), (+X-Y)
    pts = [(x1, y1 - tr), (x1 - tr, y1), (x0 + tl, y1), (x0, y1 - tl),
           (x0, y0 + bl), (x0 + bl, y0), (x1 - br, y0), (x1, y0 + br)]
    out = []
    for p in pts:
        if not out or (abs(p[0] - out[-1][0]) > 1e-9 or abs(p[1] - out[-1][1]) > 1e-9):
            out.append(p)
    return np.array([(m(x), m(y)) for x, y in out])


def plate(poly_mm, z_front, z_back, mat):
    """凸な多角形（mm、反時計回りでも時計回りでもよい）を z_front〜z_back に押し出した板。"""
    P = np.array([(m(x), m(y)) for x, y in poly_mm])
    zf, zb = m(z_front), m(z_back)
    n = len(P)
    for i in range(1, n - 1):
        tri = [P[0], P[i], P[i + 1]]
        _add(mesh, mat, [(p[0], p[1], zf) for p in tri], [(0, 0, -1)] * 3,
             [((p[0] - P[:, 0].min()) * 20, (p[1] - P[:, 1].min()) * 20) for p in tri])
    cen = P.mean(0)
    for i in range(n):
        a, b = P[i], P[(i + 1) % n]
        e = b - a
        nrm = np.array([e[1], -e[0], 0.0])
        nrm /= np.linalg.norm(nrm)
        if np.dot(nrm[:2], (a + b) / 2 - cen) < 0:
            nrm = -nrm
        _add(mesh, mat, [(a[0], a[1], zf), (b[0], b[1], zf), (b[0], b[1], zb), (a[0], a[1], zb)],
             [tuple(nrm)] * 4, [(0, 0), (1, 0), (1, 1), (0, 1)])


# ---------------------------------------------------------------- 本体
hole = rounded_rect(m(hw), m(hh), m(0.5), *c2)
cy = MON_CY
lip = chamfer_rect(-hw - LIP, hw + LIP, cy - hh - LIP, cy + hh + LIP, (3, 3, 3, 3))
# 左上（OBJ の +X+Y）と右下（OBJ の -X-Y）を大きく切り落とす
outline = chamfer_rect(-CASE_HW, CASE_HW, CASE_BOTTOM, CASE_TOP, (CUT_BIG, CUT_SMALL, CUT_BIG, CUT_SMALL))

corner_angles = []
for poly in (hole, lip, outline):
    for p in poly:
        corner_angles.append(math.atan2(p[1] - c2[1], p[0] - c2[0]))
angles = sorted(set(list(np.linspace(-math.pi, math.pi, 721)) + corner_angles))
angles = [a for a in angles if a < math.pi] + [angles[0] + 2 * math.pi]

# マテリアルの並びを前と同じにするため、Trim → Bezel → MonitorBack の順に作る
_, lip_ring = ring_face(mesh, hole, lip, c2, m(Z0 - LIP_OUT), 'Trim', angles)
hole_ring, _ = ring_face(Mesh(), hole, lip, c2, 0, 'Trim', angles)
if WITH_CASE:
    _, out_ring = ring_face(mesh, lip, outline, c2, m(Z0), 'Bezel', angles)
    extrude_outline(mesh, lip_ring, m(Z0 - LIP_OUT), m(Z0), 'Trim', outward=True)
    extrude_outline(mesh, out_ring, m(Z0), m(Z1), 'Bezel', outward=True)
else:
    # 縁だけ：黒い縁を奥（背面）まで伸ばした枠にする。外側の側面と背面は Bezel（暗いグレー）
    extrude_outline(mesh, lip_ring, m(Z0 - LIP_OUT), m(Z1), 'Bezel', outward=True)
    ring_face(mesh, hole, lip, c2, m(Z1), 'Bezel', angles, normal=(0, 0, 1))
extrude_outline(mesh, hole_ring, m(Z0 - LIP_OUT), m(Z0 + 6), 'MonitorBack', outward=False)
box(mesh, m(-hw, cy - hh, Z0 + 6), m(hw, cy + hh, Z1), 'MonitorBack')

if WITH_CASE:
    zf, zb = Z0 - DECO_OUT, Z0

    # ---------------------------------------------------------------- 飾り
    lip_bottom = cy - hh - LIP          # 95
    lip_top = cy + hh + LIP             # 279
    lip_side = hw + LIP                 # 209

    # 下の帯：オレンジの細い線（プレイヤーから見て左から中央すぎまで）と、右端の太いブロック
    plate([(LEFT * 196, lip_bottom - 4), (LEFT * 196, lip_bottom - 6.5), (RIGHT * 120, lip_bottom - 6.5), (RIGHT * 120, lip_bottom - 4)],
          zf, zb, 'Accent')
    plate([(RIGHT * 132, lip_bottom - 3.5), (RIGHT * 168, lip_bottom - 3.5), (RIGHT * 172, lip_bottom - 9), (RIGHT * 136, lip_bottom - 9)],
          zf, zb, 'Accent')
    # 下の帯の左寄り：白い斜めのタグ
    plate([(LEFT * 150, lip_bottom - 3.5), (LEFT * 196, lip_bottom - 3.5), (LEFT * 200, lip_bottom - 0.5), (LEFT * 154, lip_bottom - 0.5)],
          zf - 0.3, zb, 'Label')

    # 左上の大きな切り落とし：切り口に沿ったオレンジの帯
    d0, d1 = 6, 11   # 切り口から内側へのきょり
    s = CASE_HW + CASE_TOP - CUT_BIG    # 切り口の線：|x| + y = s
    band = []
    for d in (d0, d1):
        k = s - d * math.sqrt(2)
        y_hi = lip_top + 1.5
        x_hi = CASE_HW - 3
        band.append(((k - y_hi), y_hi))
        band.append((x_hi, k - x_hi))
    plate([(LEFT * band[0][0], band[0][1]), (LEFT * band[1][0], band[1][1]),
           (LEFT * band[3][0], band[3][1]), (LEFT * band[2][0], band[2][1])], zf, zb, 'Accent')

    # 右下の大きな切り落とし：切り口に沿った白い帯
    k0 = s - 6 * math.sqrt(2)
    k1 = s - 9 * math.sqrt(2)
    # 右下の切り口の線：|x| + (CASE_BOTTOM + CASE_TOP - y) = s  →  |x| - y = s - CASE_BOTTOM - CASE_TOP
    def br(k, x=None, y=None):
        c = k - CASE_BOTTOM - CASE_TOP
        return (x, x - c) if y is None else (y + c, y)
    yb = CASE_BOTTOM + 3
    p = [br(k0, y=lip_bottom - 1.5), br(k0, x=CASE_HW - 3), br(k1, x=CASE_HW - 3), br(k1, y=lip_bottom - 1.5)]
    # 下の端より下に出ないようにする
    p = [(x, max(y, yb)) for x, y in p]
    plate([(RIGHT * x, y) for x, y in p], zf, zb, 'Label')

    # 右の帯：通気口のすき間 6本
    for i in range(6):
        y = cy + 40 - i * 13
        plate([(RIGHT * 218, y), (RIGHT * 230, y), (RIGHT * 230, y - 4), (RIGHT * 218, y - 4)], zf + 0.8, zb, 'Vent')

    # 左の帯：オレンジのランプ 3つと、細い縦線
    for i in range(3):
        y = cy + 30 - i * 11
        plate([(LEFT * 220, y), (LEFT * 228, y), (LEFT * 228, y - 6), (LEFT * 220, y - 6)], zf, zb, 'Accent')
    plate([(LEFT * 223, cy - 10), (LEFT * 225, cy - 10), (LEFT * 225, cy - 70), (LEFT * 223, cy - 70)], zf, zb, 'Label')

    # ねじ（6角形）
    def screw(x, y, r=3.2):
        plate([(x + r * math.cos(a), y + r * math.sin(a)) for a in np.linspace(0, 2 * math.pi, 7)[:-1] + math.pi / 6],
              zf, zb, 'Screw')
    screw(LEFT * 226, CASE_BOTTOM + 9)
    screw(RIGHT * 226, CASE_TOP - 10)
    screw(RIGHT * 226, cy - 60)

fix_winding(mesh)
write_obj(mesh, os.path.join(OUT, 'MonitorFrame.obj'), 'MonitorFrame', MAT)
print('MonitorFrame', tri_count(mesh), '三角形', mesh.order)
