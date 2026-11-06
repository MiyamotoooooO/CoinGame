"""ショップの画面の背景：あたたかい木のレコードショップを作る（ローポリ・単色・OBJ/MTL）。
キャラクターの前と後ろで、2つのファイルに分けて書き出す：
  RecordShop_Front.obj … 手前の物（エサ箱・左右の面出しの段・手前の観葉植物）
  RecordShop_Back.obj  … 奥の物（床・壁・天井・棚・看板・植物・ミラーボール・電球）
2つとも同じ原点なので、同じ位置（0, 0, 0）に置けば元の並びになる。

・手前：レコードがぎっしり立った木のエサ箱（3つ）と、仕切りの札・後ろの段に面出しのジャケット
・奥の壁：左はレコードの背がつまった木の棚、まん中と右はジャケットを面出しで並べた棚
・観葉植物（床の鉢・つり下げの鉢）、ミラーボール、天井の電球のガーランド、はだか電球、木の梁
キャラクター（店員）は、エサ箱と奥の壁のあいだ（z = 0.7〜1.3 あたり）に置く。
単位はメートル、Y が上。カメラは -Z 側から +Z の向きに見る。原点は床のまん中。
使い方: python build_record_shop.py 出力先フォルダ
"""
import math
import os
import random
import sys

import shoptools as T
from shoptools import Mesh, BOX, DISC, ROD, ELLIPSOID, save

T.OUT = sys.argv[1] if len(sys.argv) > 1 else 'out'
os.makedirs(T.OUT, exist_ok=True)

T.MAT.update({
    'Floor': (0.50, 0.38, 0.28, 0.2),
    'Wall': (0.93, 0.89, 0.82, 0.1),
    'Ceiling': (0.82, 0.78, 0.72, 0.1),
    'Wood': (0.82, 0.62, 0.40, 0.2),
    'WoodLight': (0.90, 0.74, 0.52, 0.2),
    'WoodDark': (0.52, 0.35, 0.22, 0.2),
    'Card': (0.86, 0.70, 0.42, 0.1),
    'Ink': (0.20, 0.16, 0.12, 0.1),
    'Pot': (0.78, 0.44, 0.28, 0.2),
    'PotWhite': (0.94, 0.93, 0.90, 0.3),
    'Leaf': (0.24, 0.50, 0.24, 0.3),
    'LeafLight': (0.38, 0.64, 0.30, 0.3),
    'Cord': (0.12, 0.11, 0.10, 0.2),
    'Bulb': (1.00, 0.86, 0.55, 0.6),
    'Mirror': (0.85, 0.86, 0.90, 1.0),
    'Black': (0.07, 0.07, 0.08, 0.4),
    'Orange': (1.00, 0.48, 0.10, 0.4),
    # ジャケットの色（少しくすんだ昔の色）
    'J_Mustard': (0.88, 0.68, 0.22, 0.2),
    'J_Teal': (0.18, 0.55, 0.55, 0.2),
    'J_Brick': (0.72, 0.26, 0.20, 0.2),
    'J_Cream': (0.95, 0.90, 0.78, 0.2),
    'J_Navy': (0.16, 0.20, 0.38, 0.2),
    'J_Pink': (0.94, 0.55, 0.62, 0.2),
    'J_Sky': (0.52, 0.74, 0.88, 0.2),
    'J_Olive': (0.48, 0.52, 0.24, 0.2),
    'J_Black': (0.12, 0.12, 0.13, 0.2),
    'J_Orange': (0.96, 0.52, 0.18, 0.2),
    'J_Lilac': (0.66, 0.56, 0.82, 0.2),
})
JC = [k for k in T.MAT if k.startswith('J_')]
rng = random.Random(23)
back = Mesh()      # 奥の物
front = Mesh()     # 手前の物
m = back           # いま作っている方（下の道具は m に足す）
WIDTH, WALL_Z, CEIL = 6.4, 1.85, 3.1


def jacket(c, size, rx=0.0, ry=0.0, face=-1):
    """ジャケット1枚（地の色＋図形1つ）。c は中心、face=-1 で -Z を向く。"""
    base, deco = rng.sample(JC, 2)
    BOX(m, c, (size, size, 0.008), base, rx=rx, ry=ry)
    kind = rng.choice(['circle', 'band', 'block', 'circle'])
    R = T.rot_euler(rx, ry, 0)
    off = R @ T.np.array([0.0, 0.0, -0.0045])
    p = (c[0] + off[0], c[1] + off[1], c[2] + off[2])
    if kind == 'circle':
        DISC(m, (p[0] + rng.uniform(-0.1, 0.1) * size, p[1] + rng.uniform(-0.1, 0.1) * size, p[2]),
             size * rng.uniform(0.22, 0.34), 0.002, deco, axis=tuple(-(R @ T.np.array([0, 0, 1.0]))), seg=14)
    elif kind == 'band':
        BOX(m, p, (size, size * 0.18, 0.002), deco, rx=rx, ry=ry)
    else:
        BOX(m, (p[0], p[1] - size * 0.12, p[2]), (size * 0.6, size * 0.5, 0.002), deco, rx=rx, ry=ry)


# ------------------------------------------------------------- 床・壁・天井・梁
BOX(m, (0, -0.02, 0.4), (WIDTH, 0.04, 3.4), 'Floor')
for k in range(9):                                                                 # 床板の線
    BOX(m, (-WIDTH / 2 + 0.7 * k + 0.35, 0.001, 0.4), (0.008, 0.002, 3.4), 'WoodDark')
BOX(m, (0, CEIL / 2, WALL_Z + 0.05), (WIDTH, CEIL, 0.1), 'Wall')
for s in (-1, 1):
    BOX(m, (s * (WIDTH / 2 + 0.05), CEIL / 2, 0.4), (0.1, CEIL, 3.0), 'Wall')
BOX(m, (0, CEIL + 0.03, 0.4), (WIDTH + 0.2, 0.06, 3.2), 'Ceiling')
for z in (0.0, 0.9, 1.6):
    BOX(m, (0, CEIL - 0.06, z), (WIDTH, 0.12, 0.10), 'WoodDark')                    # 梁

# ------------------------------------------------------------- 左：レコードの背がつまった棚
LX0, LX1, LY1 = -3.15, -1.25, 2.45
SD = 0.36
COLS, ROWS = 4, 5
BOX(m, ((LX0 + LX1) / 2, LY1 / 2, WALL_Z - 0.02), (LX1 - LX0, LY1, 0.03), 'WoodDark')
for i in range(COLS + 1):
    x = LX0 + (LX1 - LX0) * i / COLS
    BOX(m, (x, LY1 / 2, WALL_Z - SD / 2), (0.03, LY1, SD), 'Wood')
for j in range(ROWS + 1):
    y = LY1 * j / ROWS
    BOX(m, ((LX0 + LX1) / 2, y, WALL_Z - SD / 2), (LX1 - LX0, 0.03, SD), 'Wood')
for i in range(COLS):
    for j in range(ROWS):
        x0 = LX0 + (LX1 - LX0) * i / COLS + 0.02
        x1 = LX0 + (LX1 - LX0) * (i + 1) / COLS - 0.02
        y0 = LY1 * j / ROWS + 0.015
        h = LY1 / ROWS - 0.06
        x = x0
        while x < x1 - 0.012:                                                      # レコードの背
            w = rng.uniform(0.006, 0.014)
            BOX(m, (x + w / 2, y0 + h * 0.5 - 0.01, WALL_Z - SD / 2 - 0.01), (w, h * 0.93, SD * 0.85), rng.choice(JC))
            x += w + 0.001
        if rng.random() < 0.35:                                                    # ところどころ面出し
            jacket(((x0 + x1) / 2, y0 + h * 0.5, WALL_Z - SD - 0.01), min(h, x1 - x0) * 0.9, rx=-6)

# ------------------------------------------------------------- まん中と右：面出しのジャケットの棚
def face_wall(x0, x1, rows, y0, dy, size):
    for r in range(rows):
        y = y0 + r * dy
        BOX(m, ((x0 + x1) / 2, y, WALL_Z - 0.07), (x1 - x0, 0.03, 0.14), 'Wood')     # 棚板
        BOX(m, ((x0 + x1) / 2, y + 0.04, WALL_Z - 0.135), (x1 - x0, 0.05, 0.012), 'Wood')  # ふち
        n = int((x1 - x0) // (size + 0.03))
        gap = ((x1 - x0) - n * size) / (n + 1)
        for k in range(n):
            cx = x0 + gap + size / 2 + k * (size + gap)
            jacket((cx, y + 0.015 + size / 2, WALL_Z - 0.09 + size * 0.08), size, rx=-8)


BOX(m, (0, 1.75, WALL_Z - 0.02), (2.3, 1.6, 0.02), 'WoodLight')                    # まん中の板
face_wall(-1.10, 1.10, 3, 1.02, 0.50, 0.31)
BOX(m, (2.15, 1.55, WALL_Z - 0.02), (1.9, 2.3, 0.02), 'WoodLight')                 # 右の板
face_wall(1.25, 3.05, 4, 0.55, 0.50, 0.31)
# 看板（レコードのマーク）
BOX(m, (0, 2.75, WALL_Z - 0.03), (1.4, 0.32, 0.03), 'WoodDark')
DISC(m, (-0.48, 2.75, WALL_Z - 0.05), 0.13, 0.015, 'Black', seg=20)
DISC(m, (-0.48, 2.75, WALL_Z - 0.06), 0.045, 0.004, 'Orange', seg=12)
for k, w in enumerate((0.70, 0.50)):
    BOX(m, (0.12 - (0.70 - w) / 2, 2.80 - k * 0.11, WALL_Z - 0.05), (w, 0.06, 0.005), 'WoodLight')

# ------------------------------------------------------------- 手前：エサ箱（レコードが立ったトレイ）
m = front
BIN_Y, BIN_D = 0.55, 0.62
for b in range(3):
    cx = -1.85 + b * 1.85
    bw = 1.70
    z0, z1 = -0.45, -0.45 + BIN_D
    BOX(m, (cx, BIN_Y / 2, (z0 + z1) / 2), (bw, BIN_Y, BIN_D), 'Wood')               # 台
    BOX(m, (cx, BIN_Y + 0.10, z0 + 0.015), (bw, 0.20, 0.03), 'WoodLight')            # 手前のふち
    BOX(m, (cx, BIN_Y + 0.10, z1 - 0.015), (bw, 0.20, 0.03), 'WoodLight')            # 奥のふち
    for s in (-1, 1):
        BOX(m, (cx + s * (bw / 2 - 0.015), BIN_Y + 0.10, (z0 + z1) / 2), (0.03, 0.20, BIN_D), 'WoodLight')
    BOX(m, (cx, BIN_Y + 0.10, (z0 + z1) / 2), (0.03, 0.20, BIN_D), 'WoodLight')       # まん中の仕切り
    for half in (-1, 1):
        hx0 = cx + (0.02 if half > 0 else -bw / 2 + 0.03)
        hx1 = cx + (bw / 2 - 0.03 if half > 0 else -0.02)
        ncol = max(1, int((hx1 - hx0) // 0.34))
        cw = (hx1 - hx0) / ncol
        for col in range(ncol):                                                      # 横にならぶ列
            ccx = hx0 + cw * (col + 0.5)
            for row in range(2):                                                     # 前後2段
                rz0 = z0 + 0.04 + row * (BIN_D / 2 - 0.02)
                for k in range(18):                                                  # 立てたレコード（奥へ少し倒す）
                    z = rz0 + k * 0.013
                    if z > rz0 + BIN_D / 2 - 0.08:
                        break
                    BOX(m, (ccx, BIN_Y + 0.155, z), (0.31, 0.31, 0.006), rng.choice(JC), rx=-12)
                jacket((ccx, BIN_Y + 0.17, rz0 - 0.006), 0.31, rx=-22)              # いちばん手前の1枚
        tx = hx0 + rng.uniform(0.05, (hx1 - hx0) - 0.25)                             # 仕切りの札
        BOX(m, (tx + 0.10, BIN_Y + 0.34, z0 + 0.06), (0.20, 0.07, 0.004), 'Card', rx=-12)
        BOX(m, (tx + 0.09, BIN_Y + 0.345, z0 + 0.056), (0.13, 0.015, 0.002), 'Ink', rx=-12)
# エサ箱の後ろの段（面出しのジャケット）。キャラクターの顔が隠れないよう、左右のはしだけに置く
for x0 in (-2.75, 1.65):
    BOX(m, (x0 + 0.55, BIN_Y + 0.28, -0.45 + BIN_D + 0.05), (1.15, 0.05, 0.10), 'WoodLight')
    for k in range(3):
        jacket((x0 + 0.18 + k * 0.37, BIN_Y + 0.47, -0.45 + BIN_D + 0.07), 0.33, rx=-10)

m = back

# ------------------------------------------------------------- 観葉植物
def plant(x, z, h, scale=1.0, pot='Pot'):
    ROD(m, (x, 0, z), (x, 0.32 * scale, z), 0.16 * scale, pot, seg=12, r1=0.19 * scale)  # 鉢
    for k in range(9):
        a = k * 40 + rng.uniform(-10, 10)
        lean = rng.uniform(25, 55)
        R = T.rot_euler(0, a, 0) @ T.rot_euler(lean, 0, 0)
        stem_top = T.np.array([x, 0.3 * scale, z]) + R @ T.np.array([0, h * rng.uniform(0.6, 1.0), 0])
        ROD(m, (x, 0.3 * scale, z), tuple(stem_top), 0.008, 'Leaf', seg=4)
        ELLIPSOID(m, tuple(stem_top), (0.14 * scale, 0.025, 0.24 * scale), rng.choice(['Leaf', 'LeafLight']), seg=6, rings=3,
                  R=T.rot_euler(-lean * 0.6, a, 0))


plant(-2.85, 1.25, 0.9, 1.1)
plant(2.9, 1.2, 1.2, 1.2, 'PotWhite')
m = front
plant(-3.0, -0.75, 0.7, 0.9)      # 手前の植物
m = back


def hanging_plant(x, y, z):
    ROD(m, (x, CEIL - 0.06, z), (x, y + 0.18, z), 0.004, 'Cord', seg=4)
    ROD(m, (x, y, z), (x, y + 0.18, z), 0.11, 'PotWhite', seg=10, r1=0.13)
    for k in range(7):                                                             # 垂れ下がるつる
        a = math.radians(k * 51)
        pts = []
        for j in range(5):
            r = 0.12 + j * 0.02
            pts.append((x + r * math.cos(a), y + 0.15 - j * 0.12 - rng.uniform(0, 0.04), z + r * math.sin(a)))
        for p in pts:
            ELLIPSOID(m, p, (0.05, 0.03, 0.05), rng.choice(['Leaf', 'LeafLight']), seg=5, rings=3)


hanging_plant(-2.2, 2.25, 0.5)
hanging_plant(1.9, 2.35, 0.7)
hanging_plant(0.2, 2.45, 0.2)

# ------------------------------------------------------------- ミラーボール・電球
ROD(m, (-1.2, CEIL - 0.06, 0.6), (-1.2, 2.55, 0.6), 0.005, 'Cord', seg=4)
ELLIPSOID(m, (-1.2, 2.40, 0.6), (0.17, 0.17, 0.17), 'Mirror', seg=12, rings=8)
for (x, z, y) in ((-0.4, 0.3, 2.55), (0.9, 0.4, 2.65), (2.6, 0.2, 2.6), (-2.7, 0.2, 2.6)):    # はだか電球
    ROD(m, (x, CEIL - 0.06, z), (x, y + 0.06, z), 0.006, 'Cord', seg=4)
    ROD(m, (x, y + 0.06, z), (x, y + 0.10, z), 0.02, 'Black', seg=6)
    ELLIPSOID(m, (x, y, z), (0.045, 0.06, 0.045), 'Bulb', seg=8, rings=5)
# 電球のガーランド（天井からゆるく垂れる線に、小さな電球）
for (xa, xb, z) in ((-3.1, 0.2, 1.2), (0.2, 3.1, 1.0), (-3.1, 3.1, 0.0)):
    pts = []
    n = 20
    for i in range(n + 1):
        t = i / n
        x = xa + (xb - xa) * t
        y = CEIL - 0.12 - 0.35 * math.sin(math.pi * t)
        pts.append((x, y, z))
    T.PATH(m, pts, 0.004, 'Cord', sides=4)
    for i in range(1, n, 2):
        x, y, zz = pts[i]
        ELLIPSOID(m, (x, y - 0.035, zz), (0.022, 0.03, 0.022), 'Bulb', seg=6, rings=3)

save(front, 'RecordShop_Front')
save(back, 'RecordShop_Back')
print(T.stats)
