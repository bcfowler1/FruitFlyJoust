"""Physical contact qualification, including an inverted-body negative case."""
import json
from pathlib import Path
import mujoco,numpy as np
from export_body import body_model
from landing_contacts import claw_contacts
def main():
    model,data=body_model()
    for _ in range(3000):mujoco.mj_step(model,data)
    feet,other=claw_contacts(model,data)
    assert len(feet)>=4 and other==0,(feet,other)
    upright=dict(claws=len(feet),non_claw_contacts=other)
    model,data=body_model()
    data.qpos[3:7]=[0,1,0,0]
    for _ in range(3000):mujoco.mj_step(model,data)
    feet,other=claw_contacts(model,data)
    assert np.isfinite(data.qpos).all() and other>0,(feet,other)
    assert not(len(feet)>=4 and other==0)
    result=dict(qualified=True,upright=upright,inverted=dict(claws=len(feet),non_claw_contacts=other),
        body_collision_cannot_qualify=True,distinct_claws=True,live_mujoco_contacts=True)
    Path(__file__).with_name('landing-contact-evaluation.json').write_text(json.dumps(result,indent=2));print(result)
if __name__=='__main__':main()
