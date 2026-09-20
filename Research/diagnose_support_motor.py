"""Continuous loaded six-claw standing probe for engineering leg-force mapping."""
import json,time
from pathlib import Path
import mujoco
from evaluate_unified_policies import observation,tf,np,ASSETS,walk_imitation
from optimized_flight_policy import CompiledMeanPolicy
from landing_leg_ik import LandingLegIK
from landing_contacts import claw_contacts
from rider_load import RiderLoad
from grasp_motor_feedback import support_targets

def main():
    env=walk_imitation(terminal_com_dist=float('inf'),random_state=np.random.RandomState(42))
    ref=np.zeros((5000,7));ref[:,2]=.14355;ref[:,3]=1
    env.task._traj_generator.set_next_trajectory(ref,np.zeros((5000,6)))
    env.reset();physics=env.physics
    load=RiderLoad(physics).apply(physics)
    policy=CompiledMeanPolicy(tf.saved_model.load(str(ASSETS/'walking')))
    anchor=physics.model.name2id('walker/thorax','body');rng=np.random.RandomState(42)
    try:
        for index in range(350):
            action=policy(observation(env.task,physics)).mean().numpy()[0];spec=env.action_spec()
            env.task.before_step(physics,np.clip(action,spec.minimum,spec.maximum),rng);physics.step(20)
        ik=LandingLegIK(physics,env.task._walker,anchor)
        claws=[i for i in range(physics.model.nu) if 'adhere_claw_' in physics.model.id2name(i,'actuator')]
        initial_time=float(physics.data.time);rows=[];clips=0
        for index in range(500):
            feet,other=claw_contacts(physics.model,physics.data)
            targets,feedback=support_targets(physics,ik,anchor,feet,ik.initial_height,1.)
            clips+=feedback['clipped_motor_targets']
            for aid,target in targets.items():physics.data.ctrl[aid]=target
            physics.data.ctrl[claws]=1
            physics.step(20)
            feet,other=claw_contacts(physics.model,physics.data)
            if index%10==0:
                rows.append(dict(time=float(physics.data.time),claws=len(feet),body_contacts=other,
                    up=float(physics.data.xmat[anchor].reshape(3,3)[2,2]),
                    height_cm=float(physics.data.xpos[anchor,2]),speed_cm_s=float(np.linalg.norm(physics.data.qvel[:3])),feedback=feedback))
        report=dict(passed=all(x['claws']>=4 and x['body_contacts']==0 and x['up']>.9 and x['speed_cm_s']<.5 for x in rows[5:]),
            simulated_seconds=float(physics.data.time)-initial_time,one_third_rider_load=load,
            initial_height_cm=ik.initial_height,clipped_motor_targets=clips,rows=rows,
            limitations='Engineering force-to-native-motor standing diagnostic; no landing or biology qualification.')
        name='support-motor-standing-'+time.strftime('%Y%m%dT%H%M%SZ',time.gmtime())+'-evaluation.json'
        Path(__file__).with_name(name).write_text(json.dumps(report,indent=2))
        print(json.dumps(dict(report=name,passed=report['passed'],first=rows[0],last=rows[-1],clipped_motor_targets=clips)),flush=True)
    finally:env.close()

if __name__=='__main__':main()
