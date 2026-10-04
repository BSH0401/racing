# Rebuilds each Sketchfab car as a canonical glTF: centred on the wheelbase, ground at y=0, front +Z,
# scaled to a real-world length, front wheels un-steered, only the chosen car's nodes kept.
# Writes <out>/<id>/<id>.gltf (+ bin copy, resized textures, license) and <id>.json with wheel data
# in Unity space (x negated).
import json, os, shutil, sys
import numpy as np
from PIL import Image
from gltf_tree_lib import G

OUT = sys.argv[1]
SRC = 'cars'
P = os.path.join
CARS = [
    dict(id='BMW_M3_E30', src='free_bmw_m3_e30', keep=None, length=4.35, wheelNodes=[16, 26, 36, 46],
         unsteer=[14, 16, 18, 20, 24, 26, 28, 30]),
    dict(id='Porsche_930', src='porsche_911_930_turbo_1975', keep=None, length=4.29, wheelNodes=[27, 41, 31, 35]),
    dict(id='CrownVic_Taxi', src='2001_crown_victoria_taxi_game_prop', keep=None, length=5.3, merged=True),
    dict(id='CrownVic_Police', src='2001_crown_victoria_police_interceptor_game_prop', keep=None, length=5.3, merged=True),
    dict(id='Pack_Sport', src='generic_passenger_car_pack', keep=[95, 91, 87, 93, 89], length=None,
         wheelNodes=[91, 87, 93, 89], axis=(-0.585, 0.811)),
    dict(id='Pack_SUV', src='generic_passenger_car_pack', keep=[102, 106, 112, 99, 109], length=None,
         wheelNodes=[106, 112, 99, 109], axis=(-0.951, 0.309)),
]
MAXTEX = 1024
only = sys.argv[2:]


def subtree_verts(g, i):
    out = []
    if 'mesh' in g.g['nodes'][i]:
        out.append(g.verts(i))
    for c in g.g['nodes'][i].get('children', []):
        out += subtree_verts(g, c)
    return out


def bbox(v):
    return v.min(0), v.max(0)


def xf(M, v):
    return v @ M[:3, :3].T + M[:3, 3]


for car in CARS:
    if only and car['id'] not in only:
        continue
    g = G(P(SRC, car['src'], 'scene.gltf'))
    N = g.g['nodes']
    keep = car['keep'] or g.g['scenes'][0]['nodes']
    allv = np.vstack(sum([subtree_verts(g, k) for k in keep], []))

    # Wheel centres and radius in source world space.
    if car.get('wheelNodes'):
        wb = [bbox(np.vstack(subtree_verts(g, w))) for w in car['wheelNodes']]
        centres = np.array([(lo + hi) / 2 for lo, hi in wb])
        radius = np.mean([(hi - lo)[1] / 2 for lo, hi in wb])
    else:
        mi = [i for i in g.mesh_nodes() if 'wheels_and_parts' in N[i].get('name', '')][0]
        v = g.verts(mi)
        lo0, hi0 = bbox(allv)
        zmid = (lo0[2] + hi0[2]) / 2
        centres, rs = [], []
        for sz in (1, -1):
            for sx in (-1, 1):
                q = v[(np.sign(v[:, 0]) == sx) & (np.sign(v[:, 2] - zmid) == sz)
                      & (np.abs(v[:, 0]) > 0.6 * hi0[0]) & (v[:, 1] < lo0[1] + 0.5 * (hi0[1] - lo0[1]))]
                lo, hi = bbox(q)
                centres.append((lo + hi) / 2)
                rs.append((hi - lo)[1] / 2)
        centres = np.array(centres)
        radius = np.mean(rs)

    # Canonical frame: rows map world -> (left, up, forward) in glTF convention.
    f = np.array([car['axis'][0], 0, car['axis'][1]]) if 'axis' in car else np.array([0, 0, 1.0])
    f /= np.linalg.norm(f)
    up = np.array([0, 1.0, 0])
    r = np.cross(up, f)
    R = np.eye(4)
    R[:3, :3] = np.vstack([r, up, f])
    T = np.eye(4)
    T[:3, 3] = -centres.mean(0)
    C = R @ T
    lo, hi = bbox(xf(C, allv))
    s = car['length'] / (hi[2] - lo[2]) if car['length'] else 1.0
    S = np.eye(4)
    S[:3, :3] *= s
    C = S @ C
    ground = xf(C, centres)[:, 1].mean() - radius * s
    L = np.eye(4)
    L[1, 3] = -ground
    C = L @ C

    # Subset glTF with the kept nodes re-rooted under one canonical root.
    gj = g.g
    newNodes, nodeMap, meshMap, newMeshes = [], {}, {}, []
    unsteer = set(car.get('unsteer', []))

    def add(i, M=None):
        n = {k: v for k, v in N[i].items() if k not in ('children',)}
        idx = len(newNodes)
        newNodes.append(n)
        nodeMap[i] = idx
        if M is not None:
            for k in ('translation', 'rotation', 'scale', 'matrix'):
                n.pop(k, None)
            if i in unsteer:
                M = M.copy()
                M[:3, :3] = np.diag(np.linalg.norm(M[:3, :3], axis=0))
            n['matrix'] = [float(x) for x in M.T.flatten()]
        if 'mesh' in n:
            if n['mesh'] not in meshMap:
                meshMap[n['mesh']] = len(newMeshes)
                newMeshes.append(json.loads(json.dumps(gj['meshes'][n['mesh']])))
            n['mesh'] = meshMap[n['mesh']]
        ch = [add(c) for c in N[i].get('children', [])]
        if ch:
            n['children'] = ch
        return idx

    root = {'name': car['id'], 'children': []}
    newNodes.append(root)
    if unsteer:
        # Wheel nodes sit under the scene root: flatten every kept node's direct children to world space.
        tops = []
        for k in keep:
            stack = [k]
            while stack:
                i = stack.pop()
                if 'mesh' in N[i] or i in unsteer or not N[i].get('children'):
                    tops.append(i)
                else:
                    stack += N[i]['children']
        for t in tops:
            root['children'].append(add(t, C @ g.world[t]))
    else:
        for k in keep:
            root['children'].append(add(k, C @ g.world[k]))

    matMap, newMats = {}, []
    for m in newMeshes:
        for p in m['primitives']:
            if 'material' in p:
                if p['material'] not in matMap:
                    matMap[p['material']] = len(newMats)
                    newMats.append(json.loads(json.dumps(gj['materials'][p['material']])))
                p['material'] = matMap[p['material']]
    texMap, newTex, imgMap, newImgs = {}, [], {}, []

    def remap_tex(o):
        if isinstance(o, dict):
            for k, v in list(o.items()):
                if k == 'index' and isinstance(v, int):
                    if v not in texMap:
                        t = dict(gj['textures'][v])
                        src = t['source']
                        if src not in imgMap:
                            imgMap[src] = len(newImgs)
                            newImgs.append(dict(gj['images'][src]))
                        t['source'] = imgMap[src]
                        texMap[v] = len(newTex)
                        newTex.append(t)
                    o[k] = texMap[v]
                else:
                    remap_tex(v)
        elif isinstance(o, list):
            for x in o:
                remap_tex(x)

    for m in newMats:
        remap_tex(m)
    out = dict(gj)
    out.update(nodes=newNodes, meshes=newMeshes, materials=newMats, textures=newTex, images=newImgs,
               scenes=[{'name': car['id'], 'nodes': [0]}], scene=0)
    for k in ('cameras', 'animations', 'skins'):
        out.pop(k, None)
    if not newTex:
        out.pop('textures'); out.pop('images'); out.pop('samplers', None)
    d = P(OUT, car['id'])
    os.makedirs(P(d, 'textures'), exist_ok=True)
    out['buffers'] = [{'uri': car['id'] + '.bin', 'byteLength': gj['buffers'][0]['byteLength']}]
    shutil.copy(P(SRC, car['src'], 'scene.bin'), P(d, car['id'] + '.bin'))
    shutil.copy(P(SRC, car['src'], 'license.txt'), P(d, 'license.txt'))
    for im in newImgs:
        img = Image.open(P(SRC, car['src'], im['uri']))
        if max(img.size) > MAXTEX:
            img = img.resize((MAXTEX, MAXTEX), Image.LANCZOS)
        img.save(P(d, im['uri']), optimize=True)
    with open(P(d, car['id'] + '.gltf'), 'w') as fh:
        json.dump(out, fh, indent=1)

    # Wheel data in Unity space (x negated), ordered FL, FR, RL, RR.
    wc = xf(C, centres)
    wc[:, 0] *= -1
    front = sorted([w for w in wc if w[2] > 0], key=lambda w: w[0])
    back = sorted([w for w in wc if w[2] <= 0], key=lambda w: w[0])
    tv = xf(C, allv)
    tv[:, 0] *= -1
    lo, hi = bbox(tv)
    meta = dict(wheels=[[float(x) for x in w] for w in front + back], radius=float(radius * s),
                bodyMin=[float(x) for x in lo], bodyMax=[float(x) for x in hi])
    with open(P(d, car['id'] + '.json'), 'w') as fh:
        json.dump(meta, fh, indent=1)
    print(car['id'], 'scale', round(s, 3), 'wheels', np.round(front + back, 2).tolist(), 'r', round(radius * s, 3),
          'bounds', lo.round(2), hi.round(2))
