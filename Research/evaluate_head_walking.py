"""Loaded live head actuation plus perch/unload/gait on one MuJoCo body."""
import json
from pathlib import Path
import numpy as np
from walking_backend import WalkingBody
from walking_perch import WalkingPerch
def main():
    body=WalkingBody(optimized=True,head_stabilization=True)
    try:
        perch=WalkingPerch(body);signals=[];angles=[]
        for _ in range(60):
            perch.step([.7,.7]);signals.append(body.fly._last_neck_actuation.copy())
            angles.append(body.sim.physics.bind([body.fly.model.find('joint',name) for name in ('joint_Head_yaw','joint_Head')]).qpos.copy())
            assert not body.ended and np.isfinite(body.d.qpos).all()
        assert np.max(np.abs(signals))>.001 and np.max(np.abs(angles))>.001
        assert np.max(np.abs(angles))<.6
        perch.land()
        for _ in range(180):
            perch.step([0,0])
            if perch.phase=='perched':break
        assert perch.phase=='perched' and len(body.contacts())>=3
        body.set_rider_attached(False);clock=float(body.d.time);perch.resume_walk()
        for _ in range(15):perch.step([.55,.55]);assert not body.ended
        assert body.d.time>clock and np.isfinite(body.d.qpos).all()
        perch.land()
        for _ in range(180):
            perch.step([0,0])
            if perch.phase=='perched':break
        assert perch.phase=='perched' and len(body.contacts())>=3
        clock=float(body.d.time);body.set_rider_attached(True);assert body.d.time==clock
        result=dict(qualified=True,trained_upstream_head_controller=True,live_neck_actuators=True,
            maximum_neck_target_rad=float(np.max(np.abs(signals))),maximum_neck_joint_angle_rad=float(np.max(np.abs(angles))),
            loaded_gait=True,unloaded_gait=True,perch_remount=True,landed_claws=len(body.contacts()),
            floor_only=True,flywire_head_decoder=False,whole_animal_biological_validation=False)
        Path(__file__).with_name('head-walking-evaluation.json').write_text(json.dumps(result,indent=2));print(result,flush=True)
    finally:body.close()
if __name__=='__main__':main()
