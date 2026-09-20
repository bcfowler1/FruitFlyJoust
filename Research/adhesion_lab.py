"""Actual six-claw MuJoCo adhesion experiment in surface-relative coordinates."""
import json
from pathlib import Path
from types import SimpleNamespace
import mujoco, numpy as np
from export_body import body_model
from rider_load import RiderLoad

def physics_wrapper(model,data):
    return SimpleNamespace(model=SimpleNamespace(ptr=model),data=SimpleNamespace(ptr=data),
        forward=lambda:mujoco.mj_forward(model,data))

def claw_contacts(model,data):
    floor=mujoco.mj_name2id(model,mujoco.mjtObj.mjOBJ_GEOM,'floor')
    claws=set()
    for contact in data.contact:
        if floor not in (contact.geom1,contact.geom2): continue
        other=contact.geom2 if contact.geom1 == floor else contact.geom1
        name=mujoco.mj_id2name(model,mujoco.mjtObj.mjOBJ_GEOM,other) or ''
        if name.startswith('tarsal_claw_'): claws.add(name)
    return claws

def trial(surface,adhesion):
    model,data=body_model()
    load=RiderLoad(physics_wrapper(model,data)).apply(physics_wrapper(model,data))
    actuators=[i for i in range(model.nu) if (mujoco.mj_id2name(model,mujoco.mjtObj.mjOBJ_ACTUATOR,i) or '').startswith('adhere_claw')]
    if len(actuators)!=6: raise RuntimeError('Six claw actuators required')
    for _ in range(3000): mujoco.mj_step(model,data)
    initial=data.qpos[:3].copy()
    initial_contacts=len(claw_contacts(model,data))
    # Rotating gravity is physically equivalent to rotating the fly and flat surface together.
    model.opt.gravity[:]=dict(floor=(0,0,-981),wall=(-981,0,0),ceiling=(0,0,981))[surface]
    data.ctrl[actuators]=1 if adhesion else 0
    minimum=6;maximum_slip=0.
    for _ in range(5000):
        mujoco.mj_step(model,data)
        if not np.isfinite(data.qpos).all(): raise RuntimeError('Non-finite adhesion state')
        minimum=min(minimum,len(claw_contacts(model,data)))
        maximum_slip=max(maximum_slip,float(np.linalg.norm(data.qpos[:3]-initial))*.01)
    return dict(surface=surface,adhesion=adhesion,rider_load=load,
        claw_actuators=len(actuators),initial_contacts=initial_contacts,minimum_contacting_claws=minimum,
        final_contacting_claws=len(claw_contacts(model,data)),max_root_displacement_m=maximum_slip,
        simulated_seconds=.5,finite=True,attached=len(claw_contacts(model,data))>=3 and maximum_slip<.002)

def main():
    trials=[trial(surface,on) for surface in ('floor','wall','ceiling') for on in (True,False)]
    report=dict(status='contact experiments executed',trials=trials,biologically_validated=False,
        mechanism='Upstream contact-only MuJoCo adhesion actuators on six distinct tarsal claws. No root welding.',
        limitations='Flat surface, fixed leg joint targets, no approach/landing controller, uncalibrated adhesive gain. Surface-relative gravity test.')
    Path(__file__).with_name('adhesion-evaluation.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report),flush=True)

if __name__=='__main__':main()
