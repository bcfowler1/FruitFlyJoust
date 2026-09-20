"""Counterfactual engineering handoff selection; never advances the real body."""
import numpy as np
from landing_contacts import claw_contacts


def predict_native_handoff(walk, physics, policy, observe, rng, anchor, wings,
                           claws, dynamics, speed, settle_seconds, release_seconds,blend_seconds=.25):
    task=walk.task;walker=task._walker
    original_refs=(task._ref_qpos,task._ref_qvel)
    original_counter=task._step_counter
    original_action=walker._prev_action.copy();random_state=rng.get_state()
    original_state={key:getattr(physics.data,key).copy() for key in ('qpos','qvel','act','ctrl')}
    original_time=float(physics.data.time)
    trial=physics.copy(share_model=False)
    try:
        trial.model.actuator_dyntype[:]=dynamics
        settle_ok=True
        for _ in range(round(settle_seconds/.002)):
            trial.step(40)
            feet,other=claw_contacts(trial.model,trial.data)
            settle_ok &= len(feet)>=4 and other==0 and trial.data.xmat[anchor].reshape(3,3)[2,2]>.9 and np.linalg.norm(trial.data.qvel[:3])<.5
        task._ref_qpos=original_refs[0].copy();task._ref_qvel=original_refs[1].copy()
        task._ref_qpos[:,:2]=trial.data.qpos[:2]
        step=int(round(trial.data.time/.002))
        heading=trial.data.xmat[anchor].reshape(3,3)[:2,0].copy();heading/=np.linalg.norm(heading)
        yaw=float(np.arctan2(heading[1],heading[0]))
        reference_time=np.maximum(0.,(np.arange(len(task._ref_qpos))-step)*.002)
        distance=speed*np.where(reference_time<.25,reference_time**2/.5,reference_time-.125)
        task._ref_qpos[:,:2]+=distance[:,None]*heading
        heading_quat=np.array([np.cos(yaw/2),0,0,np.sin(yaw/2)])
        if np.dot(heading_quat,trial.data.qpos[3:7])<0:heading_quat=-heading_quat
        task._ref_qpos[:,3:7]=heading_quat
        task._ref_qvel[:]=0;task._ref_qvel[:,:2]=(speed*np.minimum(1.,reference_time/.25))[:,None]*heading
        task._step_counter=step
        stance=trial.data.ctrl.copy()
        for ids,selection in walker._physical_action_mapping:walker._prev_action[selection]=stance[ids]
        origin=trial.data.xpos[anchor].copy();tail=None;up=[]
        for index in range(250):
            if index==125:tail=trial.data.xpos[anchor].copy()
            action=policy(observe(task,trial)).mean().numpy()[0]
            spec=walk.action_spec();task.before_step(trial,np.clip(action,spec.minimum,spec.maximum),rng)
            native_claws=trial.data.ctrl[claws].copy()
            blend=min(1.,index*.002/blend_seconds) if blend_seconds else 1.
            trial.data.ctrl[:]=stance*(1-blend)+trial.data.ctrl*blend
            trial.data.ctrl[claws]=native_claws
            if release_seconds:trial.data.ctrl[claws]=np.maximum(native_claws,max(0.,1-index*.002/release_seconds))
            trial.data.ctrl[wings]=0;trial.step(40)
            for ids,selection in walker._physical_action_mapping:walker._prev_action[selection]=trial.data.ctrl[ids]
            up.append(float(trial.data.xmat[anchor].reshape(3,3)[2,2]))
            if up[-1]<=.85 or not np.isfinite(trial.data.qpos).all():break
        displacement=trial.data.xpos[anchor]-origin
        forward=float(displacement[:2]@heading)
        tail_forward=0. if tail is None else float((trial.data.xpos[anchor]-tail)[:2]@heading)
        qualified=bool(settle_ok and len(up)==250 and min(up)>.85 and np.linalg.norm(displacement[:2])>.01 and forward>.01 and tail_forward>.005 and np.isfinite(trial.data.qvel).all())
        return dict(qualified=qualified,completed_seconds=len(up)*.002,minimum_up=min(up),
                    forward_cm=forward,final_quarter_forward_cm=tail_forward,filter_settle_qualified=bool(settle_ok),
                    reference_heading_quaternion=heading_quat.tolist(),selection='native handoff' if qualified else 'physical posture/front-foot recovery',
                    limitations='Copied-model engineering prediction; no neural controller or biological validation.')
    finally:
        task._ref_qpos,task._ref_qvel=original_refs;task._step_counter=original_counter
        walker._prev_action[:]=original_action;rng.set_state(random_state)
        trial.free()
        assert float(physics.data.time)==original_time
        assert all(np.array_equal(getattr(physics.data,key),value) for key,value in original_state.items()), 'Prediction modified the real physical state'
