"""Bounded reproduction using published flight policy and measured wing pattern."""
import os
os.environ['TF_CPP_MIN_LOG_LEVEL']='2'
os.environ['TF_ENABLE_ONEDNN_OPTS']='0'
from pathlib import Path
import json
import sys
import time
cache=Path(__file__).resolve().parents[3]/'work/flight-cache'
cache.mkdir(exist_ok=True)
os.environ['MPLCONFIGDIR']=str(cache)
import numpy as np
import tensorflow as tf
tf.config.threading.set_intra_op_parallelism_threads(1)
tf.config.threading.set_inter_op_parallelism_threads(1)
import tensorflow_probability as tfp
_ = tfp.distributions.Independent  # Force lazy distribution registration before loading.
_ = tfp.distributions.Independent(tfp.distributions.Normal(tf.zeros([1]),tf.ones([1])),1)._type_spec
from tensorflow.python.framework import type_spec_registry
# Serialized TFP 0.16 type name changed to tfp.distributions.* in current TFP.
# Register the legacy name for its backward-compatible TypeSpec deserializer.
# We do not edit the checkpoint, graph operations, or policy weights.
type_spec_registry._NAME_TO_TYPE_SPEC['tensorflow_probability.python.distributions.independent.Independent_ACTTypeSpec']=type(_)

ROOT=Path(__file__).resolve().parents[3]
ASSETS=ROOT/'work/research/flight-assets'
sys.path.insert(0,str(ROOT/'work/research/flybody'))
from flybody.fly_envs import flight_imitation

def rollout(policy, index, use_policy=True, max_steps=1000, render=True, velocity_impulse_mps=0, rider_fraction=0):
    env=flight_imitation(ref_path=str(ASSETS/'flight-dataset_saccade-evasion_augmented.hdf5'),
        wpg_pattern_path=str(ASSETS/'wing_pattern_fmech.npy'), randomize_start_step=False,
        traj_indices=[index], random_state=np.random.RandomState(42))
    timestep=env.reset()
    load_report = None
    if rider_fraction:
        from rider_load import RiderLoad
        load_report = RiderLoad(env.physics, rider_fraction).apply(env.physics)
    spec=env.action_spec()
    errors=[]; heights=[]; max_action=0.; clipped=0; max_overshoot=0.; clipped_indices=set()
    initial=env.physics.data.qpos.copy()
    start=time.perf_counter()
    for step in range(max_steps):
        if step == 250 and velocity_impulse_mps:
            # MuJoCo uses cm/s; apply a declared lateral root-velocity disturbance.
            env.physics.data.qvel[1] += velocity_impulse_mps * 100
            env.physics.forward()
        if use_policy:
            observation={key:tf.convert_to_tensor(value[None],dtype=tf.float32)
                for key,value in timestep.observation.items()}
            action=np.asarray(policy(observation).mean())[0]
        else: action=np.zeros(spec.shape,dtype=spec.dtype)
        assert action.shape==spec.shape and np.isfinite(action).all()
        max_action=max(max_action,float(np.max(np.abs(action))))
        clipped+=int(np.sum((action<spec.minimum)|(action>spec.maximum)))
        clipped_indices.update(np.where((action<spec.minimum)|(action>spec.maximum))[0].tolist())
        max_overshoot=max(max_overshoot,float(np.max(np.abs(action-np.clip(action,spec.minimum,spec.maximum)))))
        timestep=env.step(np.clip(action,spec.minimum,spec.maximum).copy())
        assert np.isfinite(env.physics.data.qpos).all() and np.isfinite(env.physics.data.qvel).all()
        errors.append(float(np.linalg.norm(env.task.observables['walker/ref_displacement'](env.physics)[0]))*.01)
        heights.append(float(env.task._walker.observables.thorax_height(env.physics))*.01)
        if timestep.last(): break
    elapsed=time.perf_counter()-start
    result=dict(trajectory=index, trained_policy=use_policy, rider_load=load_report, steps=step+1,
        simulated_seconds=float(env.physics.data.time),wall_seconds=elapsed,
        mean_tracking_error_m=float(np.mean(errors)),max_tracking_error_m=max(errors),
        minimum_thorax_height_m=min(heights),final_thorax_height_m=heights[-1],
        episode_terminated=bool(timestep.last()),reference_end=bool(env.task._reached_traj_end),
        max_raw_action=max_action, clipped_action_entries=clipped,
        clipped_action_indices=sorted(clipped_indices), maximum_action_overshoot=max_overshoot,
        action_minimum=spec.minimum.tolist(),action_maximum=spec.maximum.tolist(),finite=True,
        final_qpos=env.physics.data.qpos.tolist())
    if index==1 and use_policy and render:
        from PIL import Image
        Image.fromarray(env.physics.render(height=480,width=640,camera_id=1)).save(Path(__file__).with_name('trained-flight-preview.png'))
    env.close()
    return result

def main():
    policy=tf.saved_model.load(str(ASSETS/'flight'))
    trials=[]
    for i in [0,1,2]:
        trials.append(rollout(policy,i))
        print('TRAJECTORY_COMPLETE',i,trials[-1]['simulated_seconds'],flush=True)
    baseline=rollout(policy,0,False)
    repeat=rollout(policy,0)
    reproducible=bool(np.allclose(trials[0]['final_qpos'],repeat['final_qpos'],rtol=1e-6,atol=1e-8))
    for result in trials+[baseline,repeat]: result.pop('final_qpos')
    passed=all(t['simulated_seconds']>=.1 and t['minimum_thorax_height_m']>=.002 and
        t['max_tracking_error_m']<.02 and t['clipped_action_entries']==0 for t in trials) and reproducible
    report=dict(status='passed' if passed else 'controller criteria not met',tensorflow=tf.__version__,
        trained_policy=True, measured_wing_pattern=True, neural_coupling=False,
        trials=trials,zero_policy_measured_pattern_baseline=baseline,repeat_trial=repeat,
        repeatable=reproducible, criteria='>=100 ms, height >=2 mm, tracking error <20 mm, no clipping, repeatable',
        source='https://doi.org/10.25378/janelia.25309105',asset_license='GPL-3.0-or-later',
        compatibility='TensorFlow 2.16.1 / TFP 0.24; legacy Independent TypeSpec name alias; unchanged checkpoint graph and weights',
        action_selection='deterministic distribution mean; explicitly clip to task action bounds')
    Path(__file__).with_name('trained-flight-evaluation.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report,indent=2),flush=True)

if __name__=='__main__': main()
