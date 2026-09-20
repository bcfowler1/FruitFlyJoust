"""Live-body dismounted idle gait, contact hold, bounds and load restoration qualification."""
import json
from pathlib import Path
import numpy as np
from walking_backend import WalkingBody
from walking_perch import WalkingPerch
from walking_idle import WalkingIdle
def main():
    body=WalkingBody(optimized=True)
    try:
        perch=WalkingPerch(body)
        for _ in range(30):perch.step([.7,.7])
        perch.land()
        for _ in range(150):
            perch.step([0,0])
            if perch.phase=='perched':break
        assert perch.phase=='perched'
        body.set_rider_attached(False);idle=WalkingIdle();origin=body.d.qpos[:3].copy();walked=False;maximum=0
        leg_before=body.d.qpos[7:].copy();leg_change=0
        for _ in range(420):
            speed,turn,hold=idle.update(body,perch,True,'floor')
            if hold:perch.land()
            perch.step([speed+.4*turn*speed,speed-.4*turn*speed])
            walked|=idle.state=='walking';maximum=max(maximum,float(np.linalg.norm(body.d.qpos[:2]-origin[:2])))
            leg_change=max(leg_change,float(np.max(np.abs(body.d.qpos[7:]-leg_before))))
            assert not body.rider_attached and np.isfinite(body.d.qpos).all() and not body.ended
        assert walked and maximum>.1 and maximum<3 and leg_change>.1
        for _ in range(150):
            idle.update(body,perch,False,'floor');perch.land();perch.step([0,0])
            if perch.phase=='perched':break
        assert perch.phase=='perched' and len(body.contacts())>=3
        clock=float(body.d.time);body.set_rider_attached(True);assert body.d.time==clock
        assert idle.update(body,perch,True,'floor') is None
        result=dict(qualified=True,live_articulated_gait=True,unloaded=True,maximum_excursion_mm=maximum,maximum_joint_change_rad=leg_change,final_claws=len(body.contacts()),remount_load_restored=True,biological_decision_making=False)
        Path(__file__).with_name('walking-idle-evaluation.json').write_text(json.dumps(result,indent=2));print(result)
    finally:body.close()
if __name__=='__main__':main()
