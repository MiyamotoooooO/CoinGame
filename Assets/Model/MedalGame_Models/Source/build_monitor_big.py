"""モニター（MonitorScreen.obj と MonitorFrame.obj）を作る：v6 背板の木の部分いっぱいの大きな画面。

・画面：460×276mm（5:3。UI は 1500×900 で描く）、下端 96mm、前面 z=201.5mm
・枠：画面のまわりの黒い細い縁（Trim）と、そのうしろの暗い板（Bezel）。暗い板は背板の木の部分をふさぐ
  （左右の壁と屋根に隠れる所まで広げてある）
・マテリアルの並び（Trim / Bezel / MonitorBack）は前と同じ

使い方: python build_monitor_big.py 出力先フォルダ
"""
import math
import os
import sys

import numpy as np

from lathe import Mesh, fix_winding, write_obj, tri_count
from shapes import box, quad_z
from deco import ring_face, extrude_outline, rounded_rect

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)


def m(*v):
    """mm → m"""
    return tuple(x / 1000 for x in v) if len(v) > 1 else v[0] / 1000


# ---------------------------------------------------------------- 寸法（mm）
SCR_W, SCR_H = 460, 276          # 画面（5:3）
SCR_BOTTOM = 96                  # 画面の下端（前と同じ）
SCR_CY = SCR_BOTTOM + SCR_H / 2  # 画面の中心の高さ
Z0 = 196                         # 枠の前面（前と同じ）
Z_SCREEN = 201.5                 # 画面の位置（前と同じ）
LIP = 8                          # 画面のまわりの黒い縁の太さ
LIP_OUT = 3                      # 黒い縁が前に出る量
LIP_DEPTH = 212                  # 黒い縁の奥行き（ここまで）
PLATE_Z = 236                    # 暗い板の位置（背板 z=240 のすぐ手前）
PLATE_HW = 300                   # 暗い板の横幅の半分（左右の壁に隠れる所まで）
PLATE_Y = (88, 400)              # 暗い板の下端と上端（屋根に隠れる所まで）

MAT = {
    'Trim': (0.035, 0.035, 0.040, 0.85),       # 黒い縁（つやあり）
    'Bezel': (0.045, 0.045, 0.052, 0.25),      # 背板をふさぐ暗い板
    'MonitorBack': (0.02, 0.02, 0.03, 0.2),
    'Screen': (0.25, 0.45, 0.85, 0.9),
}

mesh = Mesh()
hw, hh = SCR_W / 2, SCR_H / 2
c2 = np.array(m(0, SCR_CY))
hole = rounded_rect(m(hw), m(hh), m(0.5), *c2)
lip = rounded_rect(m(hw + LIP), m(hh + LIP), m(2), *c2)

angles = []
for poly in (hole, lip):
    for p in poly:
        angles.append(math.atan2(p[1] - c2[1], p[0] - c2[0]))
angles = sorted(set(list(np.linspace(-math.pi, math.pi, 721)) + angles))
angles = [a for a in angles if a < math.pi] + [angles[0] + 2 * math.pi]

# Trim → Bezel → MonitorBack の順に作る（シーンでのマテリアルの割り当てがずれないように）
_, lip_ring = ring_face(mesh, hole, lip, c2, m(Z0 - LIP_OUT), 'Trim', angles)
hole_ring, _ = ring_face(Mesh(), hole, lip, c2, 0, 'Trim', angles)
extrude_outline(mesh, lip_ring, m(Z0 - LIP_OUT), m(LIP_DEPTH), 'Bezel', outward=True)
box(mesh, m(-PLATE_HW, PLATE_Y[0], PLATE_Z), m(PLATE_HW, PLATE_Y[1], PLATE_Z + 3), 'Bezel')
extrude_outline(mesh, hole_ring, m(Z0 - LIP_OUT), m(Z_SCREEN + 1), 'MonitorBack', outward=False)
box(mesh, m(-hw, SCR_CY - hh, Z_SCREEN + 1), m(hw, SCR_CY + hh, LIP_DEPTH), 'MonitorBack')

fix_winding(mesh)
frame_mat = {k: MAT[k] for k in ('Trim', 'Bezel', 'MonitorBack')}
write_obj(mesh, os.path.join(OUT, 'MonitorFrame.obj'), 'MonitorFrame', frame_mat)
print('MonitorFrame', tri_count(mesh), mesh.order)

scr = Mesh()
quad_z(scr, m(-hw), m(hw), m(SCR_CY - hh), m(SCR_CY + hh), m(Z_SCREEN), 'Screen')
fix_winding(scr)
write_obj(scr, os.path.join(OUT, 'MonitorScreen.obj'), 'MonitorScreen', {'Screen': MAT['Screen']})
print('MonitorScreen', tri_count(scr))
