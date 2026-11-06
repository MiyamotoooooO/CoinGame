"""筐体の装飾用の形：曲面の壁、縁取りの帯（パイプ）、穴あきの枠。単位はメートル。"""
import math

import numpy as np

from lathe import Mesh


def _add(mesh, mat, pts, normals, uvs):
    """面を1枚追加する。normals は頂点ごと。"""
    bv, bt, bn = len(mesh.v), len(mesh.vt), len(mesh.vn)
    mesh.v.extend(tuple(map(float, p)) for p in pts)
    mesh.vt.extend(tuple(map(float, u)) for u in uvs)
    mesh.vn.extend(tuple(map(float, n)) for n in normals)
    mesh.add_face(mat, [(bv + i, bt + i, bn + i) for i in range(len(pts))])


def _unit(v):
    v = np.asarray(v, float)
    n = np.linalg.norm(v)
    return v / n if n > 1e-12 else v


def bezier(p0, p1, p2, p3, n):
    t = np.linspace(0, 1, n)[:, None]
    p0, p1, p2, p3 = (np.array(p, float) for p in (p0, p1, p2, p3))
    return ((1 - t) ** 3) * p0 + 3 * ((1 - t) ** 2) * t * p1 + 3 * (1 - t) * t ** 2 * p2 + t ** 3 * p3


def catmull_rom(keys, ts):
    """keys: [(t, value), ...]（t は昇順）。ts の位置の値をなめらかに補間する。"""
    kt = np.array([k[0] for k in keys], float)
    kv = np.array([k[1] for k in keys], float)
    out = []
    for t in ts:
        i = int(np.clip(np.searchsorted(kt, t) - 1, 0, len(kt) - 2))
        t0, t1 = kt[i], kt[i + 1]
        u = (t - t0) / (t1 - t0)
        p0 = kv[max(i - 1, 0)]
        p1, p2 = kv[i], kv[i + 1]
        p3 = kv[min(i + 2, len(kv) - 1)]
        out.append(0.5 * ((2 * p1) + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u
                          + (-p0 + 3 * p1 - 3 * p2 + p3) * u ** 3))
    return np.array(out)


def curved_side_wall(mesh, profile, front_z, back_z, outer_x, side, mat_inner, mat_cap, mat_outer,
                     z_segments=8, uv_tile=0.2):
    """左右の壁を1枚作る。

    profile: 内側の面の断面 (x, y) の列（下から上へ）。x は内側の面の位置（+側の壁の値）。
    front_z: profile の各点での、壁の手前のふちの z。
    side: +1 なら +X 側、-1 なら -X 側（x を反転する）。
    outer_x: 外側の面の x（+側の値）。
    """
    P = np.array(profile, float)
    n = len(P)
    sx = float(side)
    # 内側の面の法線（盤面の中央を向く）
    tang = np.gradient(P, axis=0)
    inward = np.stack([-tang[:, 1], tang[:, 0]], 1)
    inward = np.array([_unit(v) for v in inward])
    if inward[n // 2, 0] > 0:   # +X 側の壁は -X を向くようにする
        inward = -inward
    arc = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=1))])

    def X(x):
        return x * sx

    # 内側の曲面：断面の各区間を、手前のふちから奥まで z 方向に分割した帯で張る
    for i in range(n - 1):
        zs_a = np.linspace(front_z[i], back_z, z_segments + 1)
        zs_b = np.linspace(front_z[i + 1], back_z, z_segments + 1)
        na = (inward[i][0] * sx, inward[i][1], 0.0)
        nb = (inward[i + 1][0] * sx, inward[i + 1][1], 0.0)
        for k in range(z_segments):
            pts = [(X(P[i, 0]), P[i, 1], zs_a[k]), (X(P[i + 1, 0]), P[i + 1, 1], zs_b[k]),
                   (X(P[i + 1, 0]), P[i + 1, 1], zs_b[k + 1]), (X(P[i, 0]), P[i, 1], zs_a[k + 1])]
            uvs = [(p[2] / uv_tile, a / uv_tile) for p, a in zip(pts, (arc[i], arc[i + 1], arc[i + 1], arc[i]))]
            _add(mesh, mat_inner, pts, [na, nb, nb, na], uvs)

    # 手前のふち（内側の面と外側の面をつなぐ帯）
    for i in range(n - 1):
        pts = [(X(P[i, 0]), P[i, 1], front_z[i]), (X(outer_x), P[i, 1], front_z[i]),
               (X(outer_x), P[i + 1, 1], front_z[i + 1]), (X(P[i + 1, 0]), P[i + 1, 1], front_z[i + 1])]
        a, b, c = (np.array(p) for p in pts[:3])
        nrm = _unit(np.cross(b - a, c - a))
        if nrm[2] > 0:
            nrm = -nrm
        _add(mesh, mat_cap, pts, [nrm] * 4, [(p[0] / uv_tile, p[1] / uv_tile) for p in pts])

    # 外側の面（筐体の外側。プレイヤーからはほとんど見えない）
    for i in range(n - 1):
        pts = [(X(outer_x), P[i, 1], front_z[i]), (X(outer_x), P[i, 1], back_z),
               (X(outer_x), P[i + 1, 1], back_z), (X(outer_x), P[i + 1, 1], front_z[i + 1])]
        _add(mesh, mat_outer, pts, [(sx, 0, 0)] * 4, [(p[2] / uv_tile, p[1] / uv_tile) for p in pts])

    # てっぺんのふた
    top = n - 1
    pts = [(X(P[top, 0]), P[top, 1], front_z[top]), (X(outer_x), P[top, 1], front_z[top]),
           (X(outer_x), P[top, 1], back_z), (X(P[top, 0]), P[top, 1], back_z)]
    _add(mesh, mat_cap, pts, [(0, 1, 0)] * 4, [(p[0] / uv_tile, p[2] / uv_tile) for p in pts])

    return inward


def tube(mesh, path, radius, mat, up_hint, sides=10, cap=True):
    """path に沿って太さ radius のパイプを作る（縁取りの帯）。

    up_hint: 各点で断面の向きを決めるための参考の方向（壁の内側の法線など）。
    """
    path = np.asarray(path, float)
    n = len(path)
    tang = np.array([_unit(t) for t in np.gradient(path, axis=0)])
    rings, norms = [], []
    for i in range(n):
        T = tang[i]
        N = _unit(np.asarray(up_hint[i], float) - np.dot(up_hint[i], T) * T)
        B = np.cross(T, N)
        ring, nr = [], []
        for k in range(sides):
            a = 2 * math.pi * k / sides
            d = math.cos(a) * N + math.sin(a) * B
            ring.append(path[i] + radius * d)
            nr.append(d)
        rings.append(ring)
        norms.append(nr)
    arc = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(path, axis=0), axis=1))])
    for i in range(n - 1):
        for k in range(sides):
            k2 = (k + 1) % sides
            pts = [rings[i][k], rings[i + 1][k], rings[i + 1][k2], rings[i][k2]]
            nrm = [norms[i][k], norms[i + 1][k], norms[i + 1][k2], norms[i][k2]]
            uvs = [(arc[i] / 0.1, k / sides), (arc[i + 1] / 0.1, k / sides),
                   (arc[i + 1] / 0.1, (k + 1) / sides), (arc[i] / 0.1, (k + 1) / sides)]
            _add(mesh, mat, pts, nrm, uvs)
    if cap:
        for i, sgn in ((0, -1), (n - 1, 1)):
            c = path[i]
            nrm = sgn * tang[i]
            for k in range(sides):
                k2 = (k + 1) % sides
                pts = [c, rings[i][k], rings[i][k2]]
                _add(mesh, mat, pts, [nrm] * 3, [(0.5, 0.5)] * 3)


def _ray_hit(poly, center, ang):
    """center から角度 ang の向きに伸ばした線と、閉じた多角形 poly の交点（いちばん遠いもの）。"""
    d = np.array([math.cos(ang), math.sin(ang)])
    best = None
    m = len(poly)
    for i in range(m):
        a, b = poly[i], poly[(i + 1) % m]
        e = b - a
        den = d[0] * (-e[1]) - d[1] * (-e[0])
        if abs(den) < 1e-12:
            continue
        rhs = a - center
        t = (rhs[0] * (-e[1]) - rhs[1] * (-e[0])) / den
        s = (d[0] * rhs[1] - d[1] * rhs[0]) / den
        if t > 0 and -1e-9 <= s <= 1 + 1e-9 and (best is None or t > best):
            best = t
    return center + best * d


def ring_face(mesh, inner_poly, outer_poly, center, z, mat, angles, normal=(0, 0, -1), uv_size=0.5):
    """2つの多角形（どちらも center から見て星形）にはさまれた、平らな輪の面を z に作る。"""
    c = np.asarray(center, float)
    ip = [_ray_hit(inner_poly, c, a) for a in angles]
    op = [_ray_hit(outer_poly, c, a) for a in angles]
    for k in range(len(angles) - 1):
        quad = [ip[k], ip[k + 1], op[k + 1], op[k]]
        pts = [(p[0], p[1], z) for p in quad]
        uvs = [((p[0] - c[0]) / uv_size + 0.5, (p[1] - c[1]) / uv_size + 0.5) for p in quad]
        _add(mesh, mat, pts, [normal] * 4, uvs)
    return ip, op


def extrude_outline(mesh, ring_pts, z0, z1, mat, outward=True):
    """ring_pts（ring_face が返した点の列）を z0〜z1 に押し出した側面を作る。"""
    pts2 = np.array(ring_pts)
    cen = pts2.mean(0)
    for k in range(len(pts2) - 1):
        a, b = pts2[k], pts2[k + 1]
        e = b - a
        nrm = _unit([e[1], -e[0], 0.0])
        mid = (a + b) / 2 - cen
        if (np.dot(nrm[:2], mid) > 0) != outward:
            nrm = -nrm
        pts = [(a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1)]
        _add(mesh, mat, pts, [nrm] * 4, [(k / len(pts2), 0), ((k + 1) / len(pts2), 0),
                                          ((k + 1) / len(pts2), 1), (k / len(pts2), 1)])


def rounded_rect(hw, hh, r, cx=0.0, cy=0.0, n_corner=8):
    pts = []
    for (sx, sy, a0) in ((1, 1, 0), (-1, 1, 90), (-1, -1, 180), (1, -1, 270)):
        ccx, ccy = cx + sx * (hw - r), cy + sy * (hh - r)
        for j in range(n_corner + 1):
            a = math.radians(a0 + 90 * j / n_corner)
            pts.append((ccx + r * math.cos(a), ccy + r * math.sin(a)))
    return np.array(pts)
