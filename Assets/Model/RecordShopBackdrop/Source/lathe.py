"""断面（r, y）を Y 軸まわりに回転させてメッシュを作り、OBJ/MTL に書き出す。

単位はメートル、Y が上（Unity と同じ）。
各断面セグメントは独自の頂点リングを持つので、角はすべて硬いエッジになる。
"""
import math
import os


class Mesh:
    def __init__(self):
        self.v = []    # (x, y, z)
        self.vt = []   # (u, v)
        self.vn = []   # (x, y, z)
        self.faces = {}  # material -> [((vi, ti, ni), ...)]
        self.order = []  # material の出現順

    def add_face(self, mat, corners):
        if mat not in self.faces:
            self.faces[mat] = []
            self.order.append(mat)
        self.faces[mat].append(corners)


def lathe(mesh, profile, segments, uv_mode):
    """profile: [((r0, y0), (r1, y1), material, uv_radius), ...]

    セグメントは外側から見て反時計回りになる向き（法線が外を向く）で並べる。
    uv_mode: 'planar' なら上から見た平面投影、'side' なら側面の帯状展開。
    uv_radius: 平面投影でテクスチャ全体に収める半径。負なら U を反転（裏面用）。
    """
    for (r0, y0), (r1, y1), mat, uvr in profile:
        dr, dy = r1 - r0, y1 - y0
        length = math.hypot(dr, dy)
        if length == 0:
            continue
        # 断面上の法線（r, y）。進行方向を反時計回りに90度回したもの
        # （上面を中心→外へ進むと上向き、側面を下へ進むと外向きになる）
        nr, ny = -dy / length, dr / length
        base_v = len(mesh.v)
        base_t = len(mesh.vt)
        base_n = len(mesh.vn)
        horizontal = abs(dy) < 1e-12
        for i in range(segments + 1):
            a = 2 * math.pi * i / segments
            c, s = math.cos(a), math.sin(a)
            for r, y in ((r0, y0), (r1, y1)):
                mesh.v.append((r * c, y, r * s))
            mesh.vn.append((nr * c, ny, nr * s))
            if uv_mode == 'planar' or horizontal:
                R = abs(uvr)
                flip = -1 if uvr < 0 else 1
                for r in (r0, r1):
                    mesh.vt.append((0.5 + flip * r * c / (2 * R), 0.5 + r * s / (2 * R)))
            else:
                u = i / segments
                mesh.vt.append((u, 0.0))
                mesh.vt.append((u, 1.0))
        for i in range(segments):
            a0, a1 = base_v + 2 * i, base_v + 2 * i + 1
            b0, b1 = base_v + 2 * (i + 1), base_v + 2 * (i + 1) + 1
            t = lambda k: base_t + (k - base_v)
            n0, n1 = base_n + i, base_n + i + 1
            # 外向き法線になるように頂点順を決める
            quad = [(a0, t(a0), n0), (b0, t(b0), n1), (b1, t(b1), n1), (a1, t(a1), n0)]
            # r=0 の点で潰れた三角形を除く
            pts = []
            for q in quad:
                if not pts or mesh.v[q[0]] != mesh.v[pts[-1][0]]:
                    pts.append(q)
            if len(pts) >= 3 and mesh.v[pts[0][0]] == mesh.v[pts[-1][0]]:
                pts.pop()
            if len(pts) >= 3:
                mesh.add_face(mat, pts)


def mirror_profile(top):
    """上半分の断面（中心→外側→側面の途中まで）から、下半分を作る。

    上面の向き（外から見て）を保つため、下半分は順序と向きを反転させる。
    """
    bottom = []
    for (r0, y0), (r1, y1), mat, uvr in reversed(top):
        bottom.append(((r1, -y1), (r0, -y0), mat, -uvr))
    return bottom


def fix_winding(mesh):
    """各面の法線が頂点法線と同じ向きになるよう、必要なら頂点順を反転する。"""
    for mat in mesh.order:
        fixed = []
        for face in mesh.faces[mat]:
            p = [mesh.v[c[0]] for c in face]
            ux, uy, uz = (p[1][k] - p[0][k] for k in range(3))
            vx, vy, vz = (p[2][k] - p[0][k] for k in range(3))
            fn = (uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx)
            vn = mesh.vn[face[0][2]]
            if sum(fn[k] * vn[k] for k in range(3)) < 0:
                face = list(reversed(face))
            fixed.append(face)
        mesh.faces[mat] = fixed


def write_obj(mesh, path, name, materials):
    """materials: {name: (r, g, b, smoothness)}"""
    mtl_name = os.path.splitext(os.path.basename(path))[0] + '.mtl'
    with open(path, 'w') as f:
        f.write(f'# {name}  単位: メートル / Y-up\n')
        f.write(f'mtllib {mtl_name}\n')
        f.write(f'o {name}\n')
        for x, y, z in mesh.v:
            f.write(f'v {x:.6f} {y:.6f} {z:.6f}\n')
        for u, v in mesh.vt:
            f.write(f'vt {u:.6f} {v:.6f}\n')
        for x, y, z in mesh.vn:
            f.write(f'vn {x:.6f} {y:.6f} {z:.6f}\n')
        for mat in mesh.order:
            f.write(f'usemtl {mat}\n')
            for face in mesh.faces[mat]:
                f.write('f ' + ' '.join(f'{a + 1}/{b + 1}/{c + 1}' for a, b, c in face) + '\n')
    with open(os.path.join(os.path.dirname(path), mtl_name), 'w') as f:
        for mat in mesh.order:
            r, g, b, gloss = materials[mat]
            f.write(f'newmtl {mat}\n')
            f.write(f'Kd {r:.3f} {g:.3f} {b:.3f}\n')
            f.write(f'Ks {gloss:.3f} {gloss:.3f} {gloss:.3f}\n')
            f.write(f'Ns {10 + 490 * gloss:.0f}\n')
            f.write('d 1.0\n\n')


def tri_count(mesh):
    return sum(len(face) - 2 for mat in mesh.order for face in mesh.faces[mat])
