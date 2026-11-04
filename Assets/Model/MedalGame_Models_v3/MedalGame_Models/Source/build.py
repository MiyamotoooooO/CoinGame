"""メダルとレコードの仮モデルを作る。出力先は引数で指定。"""
import os
import sys

from lathe import Mesh, lathe, mirror_profile, fix_winding, write_obj, tri_count

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)

mm = 0.001

# ---------------------------------------------------------------- メダル
# 実物のメダルゲームのメダルに近い 直径25mm / 厚さ2.4mm
MEDAL_R = 12.5 * mm
MEDAL_UV = MEDAL_R

medal_top = [
    # 中央の盛り上がり（小さなレコードの意匠）
    ((0.0 * mm, 0.95 * mm), (2.2 * mm, 0.95 * mm), 'MedalFace', MEDAL_UV),
    ((2.2 * mm, 0.95 * mm), (2.2 * mm, 0.85 * mm), 'MedalFace', MEDAL_UV),
    ((2.2 * mm, 0.85 * mm), (3.6 * mm, 0.85 * mm), 'MedalFace', MEDAL_UV),
    ((3.6 * mm, 0.85 * mm), (4.4 * mm, 0.70 * mm), 'MedalFace', MEDAL_UV),
    # 一段下がった面
    ((4.4 * mm, 0.70 * mm), (8.6 * mm, 0.70 * mm), 'MedalFace', MEDAL_UV),
    # 刻印の溝
    ((8.6 * mm, 0.70 * mm), (8.6 * mm, 0.45 * mm), 'MedalFace', MEDAL_UV),
    ((8.6 * mm, 0.45 * mm), (9.2 * mm, 0.45 * mm), 'MedalFace', MEDAL_UV),
    ((9.2 * mm, 0.45 * mm), (9.2 * mm, 0.70 * mm), 'MedalFace', MEDAL_UV),
    ((9.2 * mm, 0.70 * mm), (10.2 * mm, 0.70 * mm), 'MedalFace', MEDAL_UV),
    # 縁（リム）
    ((10.2 * mm, 0.70 * mm), (10.7 * mm, 1.20 * mm), 'MedalRim', MEDAL_UV),
    ((10.7 * mm, 1.20 * mm), (12.0 * mm, 1.20 * mm), 'MedalRim', MEDAL_UV),
    ((12.0 * mm, 1.20 * mm), (12.5 * mm, 0.80 * mm), 'MedalRim', MEDAL_UV),
    # 側面（上半分）
    ((12.5 * mm, 0.80 * mm), (12.5 * mm, 0.00 * mm), 'MedalRim', MEDAL_UV),
]

medal_low_top = [
    ((0.0 * mm, 1.20 * mm), (11.9 * mm, 1.20 * mm), 'MedalFace', MEDAL_UV),
    ((11.9 * mm, 1.20 * mm), (12.5 * mm, 0.70 * mm), 'MedalRim', MEDAL_UV),
    ((12.5 * mm, 0.70 * mm), (12.5 * mm, 0.00 * mm), 'MedalRim', MEDAL_UV),
]

collider_top = [
    ((0.0 * mm, 1.20 * mm), (12.5 * mm, 1.20 * mm), 'Collider', MEDAL_UV),
    ((12.5 * mm, 1.20 * mm), (12.5 * mm, 0.00 * mm), 'Collider', MEDAL_UV),
]

GOLD = {
    'MedalFace': (0.86, 0.66, 0.22, 0.75),
    'MedalRim': (0.93, 0.76, 0.32, 0.85),
    'Collider': (0.2, 0.8, 0.2, 0.0),
}

medal_variants = [
    ('Medal', medal_top, 48),
    ('Medal_Low', medal_low_top, 16),
    ('Medal_Collider', collider_top, 12),
]

stats = {}
for name, top, seg in medal_variants:
    m = Mesh()
    lathe(m, top + mirror_profile(top), seg, 'planar')
    fix_winding(m)
    write_obj(m, os.path.join(OUT, f'{name}.obj'), name, GOLD)
    stats[name] = (len(m.v), tri_count(m))

# ---------------------------------------------------------------- レコード
# 12インチ盤に近い 直径300mm / 厚さ1.8mm / 中心穴7.3mm / ラベル直径100mm
REC_R = 150 * mm
HOLE_R = 3.65 * mm
LABEL_R = 50 * mm
T = 0.9 * mm        # 厚みの半分（ラベル部分）
G = 0.75 * mm       # 溝のある面の高さ
GAP = 0.78 * mm     # 曲間（溝のないつるつるの帯）の高さ

record_top = [
    # 中心穴の内壁（上半分）
    ((HOLE_R, 0.0), (HOLE_R, T), 'Vinyl', REC_R),
    # ラベル（A面）
    ((HOLE_R, T), (LABEL_R, T), 'LabelA', LABEL_R),
    # ラベルの外側の段差と、溝のない内周
    ((LABEL_R, T), (53.5 * mm, G), 'Vinyl', REC_R),
    ((53.5 * mm, G), (60 * mm, G), 'Vinyl', REC_R),
]

# 溝の帯を4曲分、つるつるの曲間で区切る（内側から外側へ）
tracks = [(60, 79), (80, 99), (100, 121), (122, 145)]
r_prev = 60
for i, (a, b) in enumerate(tracks):
    if a > r_prev:  # 曲間
        record_top.append(((r_prev * mm, G), (r_prev * mm, GAP), 'Vinyl', REC_R))
        record_top.append(((r_prev * mm, GAP), (a * mm, GAP), 'Vinyl', REC_R))
        record_top.append(((a * mm, GAP), (a * mm, G), 'Vinyl', REC_R))
    record_top.append(((a * mm, G), (b * mm, G), 'VinylGroove', REC_R))
    r_prev = b

record_top += [
    # 外周の溝なし部分と、縁の盛り上がり
    ((145 * mm, G), (148 * mm, G), 'Vinyl', REC_R),
    ((148 * mm, G), (149 * mm, T), 'Vinyl', REC_R),
    ((149 * mm, T), (149.6 * mm, T), 'Vinyl', REC_R),
    ((149.6 * mm, T), (REC_R, 0.5 * mm), 'Vinyl', REC_R),
    ((REC_R, 0.5 * mm), (REC_R, 0.0), 'Vinyl', REC_R),
]

record_bottom = mirror_profile(record_top)
record_bottom = [(p0, p1, 'LabelB' if mat == 'LabelA' else mat, uvr)
                 for p0, p1, mat, uvr in record_bottom]

VINYL = {
    'Vinyl': (0.05, 0.05, 0.06, 0.95),
    'VinylGroove': (0.09, 0.09, 0.10, 0.55),
    'LabelA': (0.95, 0.70, 0.20, 0.2),
    'LabelB': (0.36, 0.29, 0.66, 0.2),
}

rec = Mesh()
lathe(rec, record_top + record_bottom, 96, 'planar')
fix_winding(rec)
write_obj(rec, os.path.join(OUT, 'Record.obj'), 'Record', VINYL)
stats['Record'] = (len(rec.v), tri_count(rec))

for k, (nv, nt) in stats.items():
    print(f'{k:16s} 頂点 {nv:6d}  三角形 {nt:6d}')
