"""盤面・プッシャー・筐体・モニター・ターンテーブルのモデルを作る（v4：筐体とモニターの枠を作り込み）。

原点は盤面の上面の中央。プレイヤーは -Z 側に立ち、メダルは盤面の前端から手前へ落ちる。
OBJ は右手系なので、プレイヤーから見て右は -X、左は +X（Unity に取り込むと X が反転し、右が +X になる）。

使い方: python build_cabinet.py 出力先フォルダ
出力先には、組み立て位置をまとめた layout.json も書き出す。
"""
import json
import os
import sys

from lathe import Mesh, fix_winding, write_obj, tri_count
from shapes import (box, prism_x, prism_z, quad_z, cylinder, disc_profile,
                    rot_matrix, transformed, merge)
import numpy as np
from deco import bezier, catmull_rom, curved_side_wall, tube, ring_face, extrude_outline, rounded_rect

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)


def m(*v):
    """mm → m"""
    return tuple(x / 1000 for x in v) if len(v) > 1 else v[0] / 1000


# ------------------------------------------------------------------ 寸法（mm）
FIELD_W = 480          # 盤面の幅（横長にして、プレイヤー視点で画面いっぱいに見せる）
FIELD_FRONT = -110     # 盤面の前端（ここからメダルが落ちる）
FIELD_BACK = 240       # 盤面の奥端（プッシャーの下まで続く）
WALL_T = 12            # 盤面のふちの壁の厚さ
WALL_H = 45            # 盤面からのふちの壁の高さ
HW = FIELD_W / 2
WX = HW + WALL_T

SIDE_TOP_Y = 390       # 左右の壁のてっぺんの高さ
SIDE_BULGE = 80       # 左右の壁が外へふくらむ量（ベジェ曲線の制御点）
SIDE_CURL = -50        # てっぺんで内側へ巻き込む量（マイナスで盤面の上へ張り出す）
SIDE_SAMPLES = 40      # 左右の壁の曲面の細かさ
TRIM_R = 14            # 縁取りの帯の太さ（半径）
INLAY_R = 5            # 内側の細い飾り線の太さ（半径）
INLAY_DEPTH = 45       # 飾り線を、手前のふちからどれだけ奥に引くか
# 左右の壁の手前のふちの形：(高さの割合 0〜1, z)。波打つように前後する
SIDE_FRONT_KEYS = [(0.0, -167), (0.22, 10), (0.48, 105), (0.7, 90), (0.88, 30), (1.0, 50)]

PUSHER_FRONT = 60      # プッシャー前面（いちばん奥に下がった位置）
PUSHER_DEPTH = 160
PUSHER_H = 32
PUSHER_STROKE = 50     # 手前へ動く距離

BACKWALL_Z = (170, 182)
BACKWALL_Y = (PUSHER_H + 2, 90)

CATCH_FRONT = -155     # 落下口の受けの手前側
LIP_FRONT = -167
CATCH_FLOOR = -80
DIV_X = 150            # 仕切りの位置（中央の穴は幅約300mm）
DIV_T = 4

CAB_FRONT = -260
CAB_BACK = 280
BASE_BOTTOM = -420
DECK_Y = -90

MON_W, MON_H = 400, 160    # モニター画面（横長。比率は2.5:1）
MON_BORDER = 14
MON_BOTTOM = 96            # 枠の下端（奥の壁のすぐ上）
MON_FRONT = 196
MON_DEPTH = 16
MON_CY = MON_BOTTOM + MON_BORDER + MON_H / 2

# ターンテーブル：左の斜めの壁の面に貼り付け、少しプレイヤーの方へ向ける
# （参考画像の左の壁にあるルーレットと同じ位置づけ）
import math
TT_SCALE = 0.5
TT_WALL_Y = 185            # 壁のどの高さに付けるか
TT_Z = 178                 # 前後の位置
TT_TILT = 0
TT_YAW = -25               # プレイヤーの方へ向ける
# TT_ROLL（壁と同じ傾き）と TT_POS は、左右の壁の曲面を作ったあとで計算する

stats = {}


def save(mesh, name, mats):
    fix_winding(mesh)
    write_obj(mesh, os.path.join(OUT, f'{name}.obj'), name, mats)
    stats[name] = tri_count(mesh)


MAT = {
    'Field': (0.30, 0.19, 0.12, 0.45),
    'FieldWall': (0.36, 0.22, 0.13, 0.35),
    'Chute': (0.10, 0.08, 0.10, 0.3),
    'Divider': (0.90, 0.72, 0.25, 0.8),
    'Pusher': (0.32, 0.20, 0.13, 0.5),
    'PusherTop': (0.16, 0.14, 0.18, 0.5),
    'Trigger': (0.20, 0.85, 0.30, 0.0),
    'Cover': (0.36, 0.29, 0.66, 0.5),
    'CabinetBody': (0.11, 0.08, 0.19, 0.3),
    'WoodInner': (0.58, 0.35, 0.20, 0.3),
    'WoodBack': (0.42, 0.25, 0.15, 0.3),
    'Shroud': (0.04, 0.035, 0.05, 0.1),
    'Trim': (0.20, 0.58, 0.55, 0.6),
    'Bezel': (0.14, 0.17, 0.36, 0.5),
    'SlotDark': (0.05, 0.05, 0.07, 0.3),
    'SlotGold': (0.95, 0.71, 0.20, 0.8),
    'CabinetInner': (0.20, 0.42, 0.45, 0.4),
    'CabinetTrim': (0.95, 0.71, 0.20, 0.8),
    'MonitorFrame': (0.08, 0.08, 0.10, 0.6),
    'MonitorBack': (0.02, 0.02, 0.03, 0.2),
    'Screen': (0.25, 0.45, 0.85, 0.9),
    'Plinth': (0.14, 0.12, 0.13, 0.4),
    'PlinthTop': (0.22, 0.20, 0.22, 0.6),
    'Metal': (0.78, 0.78, 0.80, 0.9),
    'PlatterMat': (0.06, 0.06, 0.07, 0.2),
    'Button': (0.95, 0.71, 0.20, 0.6),
}


# ------------------------------------------------------------------ 盤面
pf = Mesh()
box(pf, m(-HW, -10, FIELD_FRONT), m(HW, 0, FIELD_BACK), {(0, 1, 0): 'Field', 'default': 'FieldWall'})
for s in (1, -1):  # ふちの壁（落下口の受けの外壁まで下に伸ばす）
    x0, x1 = sorted((s * HW, s * WX))
    box(pf, m(x0, CATCH_FLOOR - 10, LIP_FRONT), m(x1, WALL_H, FIELD_BACK), 'FieldWall')
box(pf, m(-HW, BACKWALL_Y[0], BACKWALL_Z[0]), m(HW, BACKWALL_Y[1], BACKWALL_Z[1]), 'FieldWall')
box(pf, m(-HW, CATCH_FLOOR - 10, LIP_FRONT), m(HW, CATCH_FLOOR, FIELD_FRONT), 'Chute')
box(pf, m(-HW, CATCH_FLOOR, LIP_FRONT), m(HW, -20, CATCH_FRONT), 'FieldWall')
save(pf, 'Playfield', MAT)

dv = Mesh()
for s in (1, -1):
    box(dv, m(s * DIV_X - DIV_T / 2, CATCH_FLOOR, CATCH_FRONT), m(s * DIV_X + DIV_T / 2, 0, FIELD_FRONT), 'Divider')
save(dv, 'Dividers', MAT)

trig = Mesh()
box(trig, m(-DIV_X + DIV_T / 2, CATCH_FLOOR + 2, CATCH_FRONT), m(DIV_X - DIV_T / 2, -40, FIELD_FRONT), 'Trigger')
save(trig, 'Hole_Center', MAT)
for name, s in (('Hole_Side_Right', -1), ('Hole_Side_Left', 1)):
    t = Mesh()
    x0, x1 = sorted((s * (DIV_X + DIV_T / 2), s * HW))
    box(t, m(x0, CATCH_FLOOR + 2, CATCH_FRONT), m(x1, -40, FIELD_FRONT), 'Trigger')
    save(t, name, MAT)

for name, s in (('SideHoleCover_Right', -1), ('SideHoleCover_Left', 1)):
    c = Mesh()
    x0, x1 = sorted((s * (DIV_X + DIV_T / 2), s * HW))
    box(c, m(x0, -4, CATCH_FRONT), m(x1, 0, FIELD_FRONT), 'Cover')
    save(c, name, MAT)

# ------------------------------------------------------------------ プッシャー
pu = Mesh()
f, b, h = PUSHER_FRONT, PUSHER_FRONT + PUSHER_DEPTH, PUSHER_H
prism_x(pu, [m(f, 0), m(b, 0), m(b, h), m(f + 8, h), m(f, h - 8)], m(-HW + 2), m(HW - 2),
        'Pusher', 'Pusher', mat_top='PusherTop')
save(pu, 'Pusher', MAT)

# ------------------------------------------------------------------ 筐体
# 左右の壁の内側の断面：盤面のふちから、外へふくらみながら立ち上がり、てっぺんで少し内側へ戻る。
# 下のほう（発射口の高さ）は盤面のふちの真上にあり、メダルの飛び方に影響しない。
side_profile = bezier((WX, WALL_H), (WX + 4, 150), (WX + SIDE_BULGE, 250), (WX + SIDE_CURL, SIDE_TOP_Y), SIDE_SAMPLES)
side_front = catmull_rom(SIDE_FRONT_KEYS, np.linspace(0, 1, SIDE_SAMPLES))
CAB_HALF = float(side_profile[:, 0].max()) + 40      # 筐体の外側（左右の壁の厚みを含む）

cab = Mesh()
body = {'default': 'CabinetBody'}
box(cab, m(-CAB_HALF, BASE_BOTTOM, CAB_FRONT), m(CAB_HALF, DECK_Y, CAB_BACK), body)
box(cab, m(-CAB_HALF - 2, DECK_Y - 24, CAB_FRONT - 2), m(CAB_HALF + 2, DECK_Y - 12, CAB_FRONT), 'Trim')
for s in (1, -1):
    inward = curved_side_wall(cab, side_profile / 1000, side_front / 1000, m(240), m(CAB_HALF), s,
                              'WoodInner', 'CabinetBody', 'CabinetBody')
    # 手前のふちに沿った太い縁取り
    rim_path, rim_up, inlay_path = [], [], []
    for (x, y), fz, (nx, ny) in zip(side_profile, side_front, inward):
        rim_path.append(m(s * (x + nx * TRIM_R * 0.4), y + ny * TRIM_R * 0.4, fz + TRIM_R * 0.3))
        inlay_path.append(m(s * (x + nx * INLAY_R * 0.3), y + ny * INLAY_R * 0.3, fz + INLAY_DEPTH))
        rim_up.append((s * nx, ny, 0.0))
    tube(cab, rim_path, m(TRIM_R), 'Trim', rim_up)
    tube(cab, inlay_path, m(INLAY_R), 'Trim', rim_up, sides=6)
    # 外板：壁の手前のふちより外側にのぞく部分を暗くふさぐ（Unity の空が見えないように）
    xs0, xs1 = sorted((s * (CAB_HALF + 1), s * (CAB_HALF + 12)))
    box(cab, m(xs0, DECK_Y, CAB_FRONT), m(xs1, SIDE_TOP_Y + 30, CAB_BACK), 'Shroud')
    # 壁の下（盤面のふちより下）を土台まで塞ぐ
    x0, x1 = sorted((s * WX, s * CAB_HALF))
    box(cab, m(x0, DECK_Y, LIP_FRONT), m(x1, WALL_H, 240), body)
# 背板と屋根（内側は木目調）
box(cab, m(-CAB_HALF, DECK_Y, 240), m(CAB_HALF, SIDE_TOP_Y + 30, CAB_BACK), {(0, 0, -1): 'WoodBack', 'default': 'CabinetBody'})
box(cab, m(-CAB_HALF, SIDE_TOP_Y, 0), m(CAB_HALF, SIDE_TOP_Y + 30, 240), {(0, -1, 0): 'Shroud', 'default': 'CabinetBody'})
tube(cab, [m(-CAB_HALF, SIDE_TOP_Y, 0), m(CAB_HALF, SIDE_TOP_Y, 0)], m(TRIM_R * 0.8), 'Trim', [(0, -1, 0)] * 2)
# モニターを背板に取り付ける台
box(cab, m(-60, MON_CY - 50, MON_FRONT + MON_DEPTH), m(60, MON_CY + 50, 240), body)

# ターンテーブル：左の壁の曲面の上、TT_WALL_Y の高さに、壁と同じ傾きで取り付ける
k = int(np.argmin(np.abs(side_profile[:, 1] - TT_WALL_Y)))
k = min(max(k, 1), SIDE_SAMPLES - 2)
dx, dy = side_profile[k + 1] - side_profile[k - 1]
TT_ROLL = math.degrees(math.atan2(dy, dx))
TT_POS = (float(side_profile[k, 0]) - 2, float(side_profile[k, 1]), TT_Z)
R_TT = rot_matrix(TT_TILT, TT_YAW, TT_ROLL, TT_SCALE)
mount = Mesh()
box(mount, m(-150, -40, -120), m(120, 0, 120), body)
for zz in (-122, 120):  # 手前と奥のふちの帯
    box(mount, m(-152, -40, zz), m(122, 2, zz + 2), 'Trim')
merge(cab, transformed(mount, R_TT, m(*TT_POS)))
save(cab, 'Cabinet', MAT)

# ------------------------------------------------------------------ モニター
# 画面（穴）の大きさと位置は前と同じ。まわりに細い縁、太い紺色の枠、下側はゆるい曲線で中央が下がる。
mon = Mesh()
hw, hh = MON_W / 2, MON_H / 2
z0, z1 = MON_FRONT, MON_FRONT + MON_DEPTH
c2 = np.array(m(0, MON_CY))
BEZEL_SIDE = hw + 30
BEZEL_TOP = hh + MON_BORDER          # 上側は前と同じ高さ（ライトバーの位置を変えないため）
BEZEL_BOTTOM = hh + 22
BEZEL_BULGE = 10                     # 左右の辺の外へのふくらみ
BEZEL_CORNER = 38                    # 角の丸み

hole = rounded_rect(m(hw), m(hh), m(0.5), *c2)
lip = rounded_rect(m(hw + 8), m(hh + 8), m(6), *c2)
outline = []
for a in np.linspace(0, 2 * math.pi, 361)[:-1]:
    # 角を丸めた長方形（スーパー楕円）に、左右のふくらみを足した形
    ca, sa = math.cos(a), math.sin(a)
    n = 7.0
    rx = BEZEL_SIDE + BEZEL_BULGE * (1 - abs(sa) ** 2)
    ry = BEZEL_TOP if sa > 0 else BEZEL_BOTTOM
    x = rx * math.copysign(abs(ca) ** (2 / n), ca)
    y = ry * math.copysign(abs(sa) ** (2 / n), sa)
    outline.append((x, y))
outline = np.array([(m(x) + c2[0], m(y) + c2[1]) for x, y in outline])

corner_angles = []
for poly in (hole, lip):
    for p in poly:
        corner_angles.append(math.atan2(p[1] - c2[1], p[0] - c2[0]))
angles = sorted(set(list(np.linspace(-math.pi, math.pi, 721)) + corner_angles))
angles = [a for a in angles if a < math.pi] + [angles[0] + 2 * math.pi]

_, lip_ring = ring_face(mon, hole, lip, c2, m(z0 - 3), 'Trim', angles)
_, out_ring = ring_face(mon, lip, outline, c2, m(z0), 'Bezel', angles)
hole_ring, _ = ring_face(Mesh(), hole, lip, c2, 0, 'Trim', angles)
extrude_outline(mon, lip_ring, m(z0 - 3), m(z0), 'Trim', outward=True)
extrude_outline(mon, hole_ring, m(z0 - 3), m(z0 + 6), 'MonitorBack', outward=False)
extrude_outline(mon, out_ring, m(z0), m(z1), 'Bezel', outward=True)
box(mon, m(-hw, MON_CY - hh, z0 + 6), m(hw, MON_CY + hh, z1), 'MonitorBack')
save(mon, 'MonitorFrame', MAT)

scr = Mesh()
quad_z(scr, m(-hw), m(hw), m(MON_CY - hh), m(MON_CY + hh), m(z0 + 5.5), 'Screen')
save(scr, 'MonitorScreen', MAT)

# ------------------------------------------------------------------ ターンテーブル
PLINTH_H = 45
ARM_PIVOT = (-105, 55)       # トーンアームの回転軸（盤面の中央寄り、奥）
BUTTON = (75, -85)

tt = Mesh()
box(tt, m(-130, 0, -100), m(100, PLINTH_H, 100), {(0, 1, 0): 'PlinthTop', 'default': 'Plinth'})
cylinder(tt, disc_profile(m(12), 0, m(4), 'Metal', 'Metal', 'Metal', bevel=m(1)),
         32, offset=(m(ARM_PIVOT[0]), m(PLINTH_H), m(ARM_PIVOT[1])))
cylinder(tt, disc_profile(m(10), 0, m(6), 'Button', 'Button', 'Button', bevel=m(1.5)),
         24, offset=(m(BUTTON[0]), m(PLINTH_H), m(BUTTON[1])))
box(tt, m(ARM_PIVOT[0] - 4, PLINTH_H, -60), m(ARM_PIVOT[0] + 4, PLINTH_H + 20, -52), 'Metal')
save(tt, 'Turntable_Base', MAT)

PLATTER_R = 78
PLATTER_H = 12
pl = Mesh()
cylinder(pl, [
    ((0, m(PLATTER_H)), (m(PLATTER_R - 2), m(PLATTER_H)), 'PlatterMat', m(PLATTER_R)),
    ((m(PLATTER_R - 2), m(PLATTER_H)), (m(PLATTER_R), m(PLATTER_H - 2)), 'Metal', m(PLATTER_R)),
    ((m(PLATTER_R), m(PLATTER_H - 2)), (m(PLATTER_R), 0), 'Metal', m(PLATTER_R)),
    ((m(PLATTER_R), 0), (0, 0), 'Metal', m(PLATTER_R)),
], 96)
cylinder(pl, [
    ((0, m(PLATTER_H + 6)), (m(1.2), m(PLATTER_H + 6)), 'Metal', m(2)),
    ((m(1.2), m(PLATTER_H + 6)), (m(1.7), m(PLATTER_H + 5)), 'Metal', m(2)),
    ((m(1.7), m(PLATTER_H + 5)), (m(1.7), m(PLATTER_H)), 'Metal', m(2)),
    ((m(1.7), m(PLATTER_H)), (0, m(PLATTER_H)), 'Metal', m(2)),
], 16)
save(pl, 'Turntable_Platter', MAT)

arm = Mesh()
cylinder(arm, disc_profile(m(5), m(4), m(26), 'Metal', 'Metal', 'Metal', bevel=m(1)), 24)
box(arm, m(-2.5, 20, -128), m(2.5, 24, 6), 'Metal')
box(arm, m(-8, 14, 6), m(8, 30, 30), 'Plinth')
box(arm, m(-7, 14, -150), m(7, 20, -126), 'Plinth')
box(arm, m(-1, 13, -144), m(1, 14, -142), 'Metal')
save(arm, 'Turntable_Tonearm', MAT)

# ------------------------------------------------------------------ 組み立て位置（確認用レンダラーと README で使う）
layout = {
    'field_half_w': HW, 'field_front': FIELD_FRONT, 'pusher_front': PUSHER_FRONT,
    'pusher_depth': PUSHER_DEPTH, 'pusher_h': PUSHER_H, 'pusher_stroke': PUSHER_STROKE,
    'tt_pos': TT_POS, 'tt_tilt': TT_TILT, 'tt_yaw': TT_YAW, 'tt_roll': TT_ROLL, 'tt_scale': TT_SCALE,
    'platter_offset': (0, PLINTH_H, 0), 'arm_pivot': (ARM_PIVOT[0], PLINTH_H, ARM_PIVOT[1]),
    'arm_play_yaw': -40, 'record_offset': (0, PLATTER_H + 0.45, 0), 'record_scale': 0.5,
}
json.dump(layout, open(os.path.join(OUT, 'layout.json'), 'w'), indent=1)

for k, n in stats.items():
    print(f'{k:22s} 三角形 {n:6d}')
