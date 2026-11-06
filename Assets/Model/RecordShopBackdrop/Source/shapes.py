"""箱・押し出し・回転体などの部品を Mesh に追加する道具。単位はメートル。

座標は OBJ（右手系、Y が上）。プレイヤーは -Z 側に立って +Z 方向を見る。
このときプレイヤーの右手側は -X になる（Unity は取り込み時に X を反転するので、
Unity 上ではプレイヤーの右手側が +X になる）。
"""
import math

from lathe import Mesh, lathe

UV_TILE = 0.2  # 箱の UV は 20cm で 1 回繰り返す


def _add_quad(mesh, mat, pts, normal, uvs):
    bv, bt, bn = len(mesh.v), len(mesh.vt), len(mesh.vn)
    mesh.v.extend(pts)
    mesh.vt.extend(uvs)
    mesh.vn.append(normal)
    mesh.add_face(mat, [(bv + i, bt + i, bn) for i in range(len(pts))])


def box(mesh, lo, hi, mat):
    x0, y0, z0 = lo
    x1, y1, z1 = hi
    t = UV_TILE
    faces = [
        ((1, 0, 0), [(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)], lambda x, y, z: (z / t, y / t)),
        ((-1, 0, 0), [(x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0)], lambda x, y, z: (z / t, y / t)),
        ((0, 1, 0), [(x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0)], lambda x, y, z: (x / t, z / t)),
        ((0, -1, 0), [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)], lambda x, y, z: (x / t, z / t)),
        ((0, 0, 1), [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)], lambda x, y, z: (x / t, y / t)),
        ((0, 0, -1), [(x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0)], lambda x, y, z: (x / t, y / t)),
    ]
    mats = mat if isinstance(mat, dict) else {}
    for n, pts, uv in faces:
        m = mats.get(n, mats.get('default')) if mats else mat
        _add_quad(mesh, m, pts, n, [uv(*p) for p in pts])


def prism_x(mesh, profile, x0, x1, mat_side, mat_cap, mat_top=None):
    """(z, y) の凸多角形を X 方向に押し出す。mat_top は上向きの面に使う。"""
    n = len(profile)
    cz = sum(p[0] for p in profile) / n
    cy = sum(p[1] for p in profile) / n
    t = UV_TILE
    for i in range(n):
        (za, ya), (zb, yb) = profile[i], profile[(i + 1) % n]
        dz, dy = zb - za, yb - ya
        L = math.hypot(dz, dy)
        nz, ny = -dy / L, dz / L
        if (za - cz) * nz + (ya - cy) * ny < 0:
            nz, ny = -nz, -ny
        pts = [(x0, ya, za), (x1, ya, za), (x1, yb, zb), (x0, yb, zb)]
        m = mat_top if (mat_top and ny > 0.7) else mat_side
        _add_quad(mesh, m, pts, (0, ny, nz), [(p[0] / t, (p[1] + p[2]) / t) for p in pts])
    for x, nx in ((x0, -1), (x1, 1)):
        pts = [(x, y, z) for z, y in profile]
        _add_quad(mesh, mat_cap, pts, (nx, 0, 0), [(p[2] / t, p[1] / t) for p in pts])


def prism_z(mesh, profile, z0, z1, mat_side, mat_cap):
    """(x, y) の凸多角形を Z 方向に押し出す（盤面の左右に沿った斜めの壁など）。"""
    n = len(profile)
    cx = sum(p[0] for p in profile) / n
    cy = sum(p[1] for p in profile) / n
    t = UV_TILE
    for i in range(n):
        (xa, ya), (xb, yb) = profile[i], profile[(i + 1) % n]
        dx, dy = xb - xa, yb - ya
        L = math.hypot(dx, dy)
        nx, ny = -dy / L, dx / L
        if (xa - cx) * nx + (ya - cy) * ny < 0:
            nx, ny = -nx, -ny
        pts = [(xa, ya, z0), (xb, yb, z0), (xb, yb, z1), (xa, ya, z1)]
        _add_quad(mesh, mat_side, pts, (nx, ny, 0), [(p[2] / t, (p[0] + p[1]) / t) for p in pts])
    for z, nz in ((z0, -1), (z1, 1)):
        pts = [(x, y, z) for x, y in profile]
        _add_quad(mesh, mat_cap, pts, (0, 0, nz), [(p[0] / t, p[1] / t) for p in pts])


def rot_matrix(rot_x_deg=0.0, rot_y_deg=0.0, rot_z_deg=0.0, scale=1.0):
    """Y 回転 × X 回転 × Z 回転（Unity のオイラー角と同じ Z→X→Y の順）に、拡大率を掛けたもの。"""
    ax, ay, az = (math.radians(a) for a in (rot_x_deg, rot_y_deg, rot_z_deg))
    Rx = [[1, 0, 0], [0, math.cos(ax), -math.sin(ax)], [0, math.sin(ax), math.cos(ax)]]
    Ry = [[math.cos(ay), 0, math.sin(ay)], [0, 1, 0], [-math.sin(ay), 0, math.cos(ay)]]
    Rz = [[math.cos(az), -math.sin(az), 0], [math.sin(az), math.cos(az), 0], [0, 0, 1]]
    mul = lambda A, B: [[sum(A[i][k] * B[k][j] for k in range(3)) for j in range(3)] for i in range(3)]
    R = mul(mul(Ry, Rx), Rz)
    return [[R[i][j] * scale for j in range(3)] for i in range(3)]


def transformed(src, R, t):
    """src の Mesh を回転 R・平行移動 t した新しい Mesh を返す。"""
    out = Mesh()
    ap = lambda p: tuple(sum(R[i][k] * p[k] for k in range(3)) + t[i] for i in range(3))
    def an(p):
        q = [sum(R[i][k] * p[k] for k in range(3)) for i in range(3)]
        n = sum(x * x for x in q) ** 0.5 or 1.0
        return tuple(x / n for x in q)
    out.v = [ap(p) for p in src.v]
    out.vt = list(src.vt)
    out.vn = [an(n) for n in src.vn]
    out.faces = {k: list(v) for k, v in src.faces.items()}
    out.order = list(src.order)
    return out


def merge(dst, src):
    bv, bt, bn = len(dst.v), len(dst.vt), len(dst.vn)
    dst.v.extend(src.v)
    dst.vt.extend(src.vt)
    dst.vn.extend(src.vn)
    for mat in src.order:
        for face in src.faces[mat]:
            dst.add_face(mat, [(a + bv, b + bt, c + bn) for a, b, c in face])


def quad_z(mesh, x0, x1, y0, y1, z, mat):
    """-Z 向き（プレイヤー側を向く）の板1枚。UV はプレイヤーから見て左下が (0,0)。

    プレイヤーの右手側は -X なので、U は X が大きいほど小さくなる。
    """
    pts = [(x1, y0, z), (x1, y1, z), (x0, y1, z), (x0, y0, z)]
    uvs = [((x1 - p[0]) / (x1 - x0), (p[1] - y0) / (y1 - y0)) for p in pts]
    _add_quad(mesh, mat, pts, (0, 0, -1), uvs)


def cylinder(mesh, profile, segments, offset=(0, 0, 0)):
    """lathe の断面で回転体を作り、offset だけずらして mesh に追加する。"""
    tmp = Mesh()
    lathe(tmp, profile, segments, 'planar')
    ox, oy, oz = offset
    bv, bt, bn = len(mesh.v), len(mesh.vt), len(mesh.vn)
    mesh.v.extend((x + ox, y + oy, z + oz) for x, y, z in tmp.v)
    mesh.vt.extend(tmp.vt)
    mesh.vn.extend(tmp.vn)
    for mat in tmp.order:
        for face in tmp.faces[mat]:
            mesh.add_face(mat, [(a + bv, b + bt, c + bn) for a, b, c in face])


def disc_profile(r, y0, y1, mat_top, mat_side, mat_bottom, bevel=0.0, uvr=None):
    """平たい円柱の断面（上面→面取り→側面→底面）。"""
    uvr = uvr or r
    p = [((0.0, y1), (r - bevel, y1), mat_top, uvr)]
    if bevel:
        p.append(((r - bevel, y1), (r, y1 - bevel), mat_side, uvr))
    p.append(((r, y1 - bevel), (r, y0), mat_side, uvr))
    p.append(((r, y0), (0.0, y0), mat_bottom, uvr))
    return p
