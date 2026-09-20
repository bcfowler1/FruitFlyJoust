"""NeuroMechFly meshes and recorded gait for the fast artistic renderer, not live physics."""
import gzip,json
from pathlib import Path
import numpy as np,mujoco
from walking_backend import WalkingBody

def main():
    body=WalkingBody(optimized=True)
    try:
        body.export()
        root=Path(__file__).resolve().parents[1]
        geometry=json.loads(gzip.decompress((root/'Assets/Resources/FlyWalkingGeometry.bytes').read_bytes()))
        poses=[]
        for i in range(60):
            body.step([.7,.7])
            if i<20:continue
            rotation=body.d.xmat[body.anchor].reshape(3,3)
            if not poses:
                for entry,g in zip(geometry['geoms'],body.geoms):
                    if 'Wing' in entry['name']:
                        entry['pivot']=((body.d.xpos[body.m.geom_bodyid[g]]-body.d.xpos[body.anchor])@rotation*.001).tolist()
            positions=(body.d.geom_xpos[body.geoms]-body.d.xpos[body.anchor])@rotation
            quats=[]
            for g in body.geoms:
                q=np.empty(4);mujoco.mju_mat2Quat(q,(rotation.T@body.d.geom_xmat[g].reshape(3,3)).ravel());quats.extend(q.tolist())
            poses.append(dict(positions=(positions*.001).ravel().tolist(),rotations=quats))
        geometry.update(poses=poses,frame_seconds=.015,description='Recorded loaded MuJoCo gait; replayed artistic motion in fast gameplay, not live brain/body simulation')
        target=root/'Assets/Resources/FlyPlayableGeometry.bytes'
        target.write_bytes(gzip.compress(json.dumps(geometry).encode(),mtime=0))
        print(json.dumps(dict(path=str(target),geoms=len(body.geoms),poses=len(poses),head_geoms=[x['name'] for x in geometry['geoms'] if 'Head' in x['name']])))
    finally:body.close()
if __name__=='__main__':main()
