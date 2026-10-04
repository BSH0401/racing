import json,os,numpy as np
def q2m(q):
    x,y,z,w=q
    return np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])
def local(n):
    if 'matrix' in n: return np.array(n['matrix']).reshape(4,4).T
    M=np.eye(4); R=q2m(n.get('rotation',[0,0,0,1])); S=np.diag(n.get('scale',[1,1,1]))
    M[:3,:3]=R@S; M[:3,3]=n.get('translation',[0,0,0]); return M
class G:
    def __init__(s,path):
        s.dir=os.path.dirname(path); s.g=json.load(open(path))
        s.bin=open(os.path.join(s.dir,s.g['buffers'][0]['uri']),'rb').read()
        s.world={}; s.parent={}
        for i,n in enumerate(s.g['nodes']):
            for c in n.get('children',[]): s.parent[c]=i
        for r in s.g['scenes'][0]['nodes']: s._walk(r,np.eye(4))
    def _walk(s,i,P):
        W=P@local(s.g['nodes'][i]); s.world[i]=W
        for c in s.g['nodes'][i].get('children',[]): s._walk(c,W)
    def acc(s,ai):
        a=s.g['accessors'][ai]; bv=s.g['bufferViews'][a['bufferView']]
        n={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[a['type']]
        dt={5126:np.float32,5125:np.uint32,5123:np.uint16,5121:np.uint8}[a['componentType']]
        off=bv.get('byteOffset',0)+a.get('byteOffset',0); stride=bv.get('byteStride',0)
        isz=np.dtype(dt).itemsize*n
        if stride and stride!=isz:
            raw=np.frombuffer(s.bin,np.uint8,a['count']*stride,off).reshape(-1,stride)[:,:isz].copy()
            return raw.view(dt).reshape(-1,n)
        return np.frombuffer(s.bin,dt,a['count']*n,off).reshape(-1,n)
    def verts(s,node):
        n=s.g['nodes'][node]; out=[]
        for p in s.g['meshes'][n['mesh']]['primitives']:
            v=s.acc(p['attributes']['POSITION']).astype(float)
            W=s.world[node]; out.append(v@W[:3,:3].T+W[:3,3])
        return np.vstack(out)
    def mesh_nodes(s): return [i for i in s.world if 'mesh' in s.g['nodes'][i]]
