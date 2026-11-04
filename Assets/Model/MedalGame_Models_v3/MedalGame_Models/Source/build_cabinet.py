"""盤面・プッシャー・筐体・モニター・ターンテーブルの仮モデルを作る。

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

GUARD_TOP_Y = 360      # 斜めの壁のてっぺんの高さ
GUARD_LEAN = 110       # 斜めの壁がてっぺんで外へ開く量
GUARD_T = 20

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

CAB_HALF = WX + GUARD_LEAN + GUARD_T + 10
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
TT_SCALE = 0.6
TT_WALL_Y = 175            # 壁のどの高さに付けるか
TT_Z = 125                 # 前後の位置
TT_ROLL = math.degrees(math.atan2(GUARD_TOP_Y - WALL_H, GUARD_LEAN))   # 壁と同じ傾き
TT_TILT = 0
TT_YAW = -25               # プレイヤーの方へ向ける
TT_POS = (WX + GUARD_LEAN * (TT_WALL_Y - WALL_H) / (GUARD_TOP_Y - WALL_H) - 2, TT_WALL_Y, TT_Z)

stats = {}


def save(mesh, name, mats):
    fix_winding(mesh)
    write_obj(mesh, os.path.join(OUT, f'{name}.obj'), name, mats)
    stats[name] = tri_count(mesh)


MAT = {
    'Field': (0.80, 0.78, 0.72, 0.55),
    'FieldWall': (0.55, 0.53, 0.60, 0.4),
    'Chute': (0.30, 0.28, 0.35, 0.3),
    'Divider': (0.90, 0.72, 0.25, 0.8),
    'Pusher': (0.75, 0.77, 0.80, 0.9),
    'PusherTop': (0.62, 0.64, 0.68, 0.7),
    'Trigger': (0.20, 0.85, 0.30, 0.0),
    'Cover': (0.36, 0.29, 0.66, 0.5),
    'CabinetBody': (0.11, 0.08, 0.19, 0.3),
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
cab = Mesh()
body = {'default': 'CabinetBody'}
box(cab, m(-CAB_HALF, BASE_BOTTOM, CAB_FRONT), m(CAB_HALF, DECK_Y, CAB_BACK), body)
box(cab, m(-CAB_HALF - 2, DECK_Y - 24, CAB_FRONT - 2), m(CAB_HALF + 2, DECK_Y - 12, CAB_FRONT), 'CabinetTrim')
# 盤面のふちから外へ開きながら立ち上がる左右の壁（プレイヤー視点で画面の左右を囲む）
for s in (1, -1):
    inner_lo, inner_hi = s * WX, s * (WX + GUARD_LEAN)
    prof = [m(inner_lo, WALL_H), m(inner_lo + s * GUARD_T, WALL_H),
            m(inner_hi + s * GUARD_T, GUARD_TOP_Y), m(inner_hi, GUARD_TOP_Y)]
    prism_z(cab, prof, m(LIP_FRONT), m(240), 'CabinetInner', 'CabinetBody')
    trim = [m(inner_hi, GUARD_TOP_Y), m(inner_hi + s * GUARD_T, GUARD_TOP_Y),
            m(inner_hi + s * GUARD_T, GUARD_TOP_Y + 10), m(inner_hi, GUARD_TOP_Y + 10)]
    prism_z(cab, trim, m(LIP_FRONT - 2), m(240), 'CabinetTrim', 'CabinetTrim')
    # 斜めの壁の外側を土台まで塞ぐ
    x0, x1 = sorted((s * WX, s * CAB_HALF))
    box(cab, m(x0, DECK_Y, LIP_FRONT), m(x1, WALL_H, 240), body)
# 背板と屋根
box(cab, m(-CAB_HALF, DECK_Y, 240), m(CAB_HALF, GUARD_TOP_Y + 30, CAB_BACK), {(0, 0, -1): 'CabinetInner', 'default': 'CabinetBody'})
box(cab, m(-CAB_HALF, GUARD_TOP_Y, 100), m(CAB_HALF, GUARD_TOP_Y + 30, 240), body)
box(cab, m(-CAB_HALF - 1, GUARD_TOP_Y, 98), m(CAB_HALF + 1, GUARD_TOP_Y + 10, 102), 'CabinetTrim')
# モニターを背板に取り付ける台
box(cab, m(-60, MON_CY - 50, MON_FRONT + MON_DEPTH), m(60, MON_CY + 50, 240), body)

# ターンテーブルの取り付け板（ターンテーブルの下に敷き、壁に埋め込む）
R_TT = rot_matrix(TT_TILT, TT_YAW, TT_ROLL, TT_SCALE)
mount = Mesh()
box(mount, m(-150, -40, -120), m(120, 0, 120), body)
for zz in (-122, 120):  # 手前と奥のふちの金色の帯
    box(mount, m(-152, -40, zz), m(122, 2, zz + 2), 'CabinetTrim')
merge(cab, transformed(mount, R_TT, m(*TT_POS)))
save(cab, 'Cabinet', MAT)

# ------------------------------------------------------------------ モニター
mon = Mesh()
hw, hh = MON_W / 2, MON_H / 2
ox, oy = hw + MON_BORDER, hh + MON_BORDER
z0, z1 = MON_FRONT, MON_FRONT + MON_DEPTH
box(mon, m(-ox, MON_CY + hh, z0), m(ox, MON_CY + oy, z1), 'MonitorFrame')
box(mon, m(-ox, MON_CY - oy, z0), m(ox, MON_CY - hh, z1), 'MonitorFrame')
box(mon, m(-ox, MON_CY - hh, z0), m(-hw, MON_CY + hh, z1), 'MonitorFrame')
box(mon, m(hw, MON_CY - hh, z0), m(ox, MON_CY + hh, z1), 'MonitorFrame')
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
