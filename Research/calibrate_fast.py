"""Fit a bounded averaged velocity response; not a reduced connectome."""
import json,time
from pathlib import Path
import numpy as np
import mujoco
from serve_flight import create_environment,ASSETS,tf
from optimized_flight_policy import CompiledMeanPolicy
from optimized_environment import cache_actuator_mapping
from rider_load import RiderLoad

def collect(policy,index):
 env=cache_actuator_mapping(create_environment(index));ts=env.reset();load=RiderLoad(env.physics).apply(env.physics)
 spec=env.action_spec();samples=[];start=time.perf_counter();window=[]
 anchor=mujoco.mj_name2id(env.physics.model.ptr,mujoco.mjtObj.mjOBJ_BODY,'walker/thorax')
 while True:
  obs={k:tf.convert_to_tensor(v[None],dtype=tf.float32) for k,v in ts.observation.items()}
  action=np.asarray(policy(obs).mean())[0];ts=env.step(np.clip(action,spec.minimum,spec.maximum).copy())
  mujoco.mj_subtreeVel(env.physics.model.ptr,env.physics.data.ptr)
  window.append(env.physics.data.subtree_linvel[anchor].copy()*.01)
  step=round(float(env.physics.data.time)/.0002)
  if step%75==0:
   ref=min(step,len(env.task._ref_qvel)-1)
   samples.append(dict(t=float(env.physics.data.time),v=np.mean(window,axis=0).tolist(),target=(env.task._ref_qvel[ref,:3]*.01).tolist()))
   window=[]
  if ts.last():break
 report=dict(index=index,wall_seconds=time.perf_counter()-start,reference_completed=bool(env.task._reached_traj_end),samples=samples,rider_mass_mg=load['rider_mass_mg'])
 env.close();return report

def main():
 policy=CompiledMeanPolicy(tf.saved_model.load(str(ASSETS/'flight')))
 trials=[collect(policy,i) for i in (0,1,2,3,4,5,6,7,8,9,48)]
 x=[];y=[]
 for trial in trials[:-1]:
  for a,b in zip(trial['samples'],trial['samples'][1:]):x.append(a);y.append(b['v'])
 y=np.asarray(y);coefficients=[]
 for axis in range(3):
  v=np.array([a['v'][axis] for a in x]);target=np.array([a['target'][axis] for a in x])
  design=np.column_stack([target-v,np.ones(len(v))])
  gain,bias=np.linalg.lstsq(design,y[:,axis]-v,rcond=None)[0]
  coefficients.append(dict(gain=float(np.clip(gain,0,1)),bias=float(bias)))
 tests=[]
 for trial in trials:
  sample=trial['samples'];v=np.array(sample[0]['v']);errors=[]
  for a,b in zip(sample,sample[1:]):
   for axis,c in enumerate(coefficients):v[axis]+=c['gain']*(a['target'][axis]-v[axis])+c['bias']
   errors.append(np.linalg.norm(v-np.array(b['v'])))
  tests.append(dict(index=trial['index'],held_out=trial['index']==48,velocity_rmse_mps=float(np.sqrt(np.mean(np.square(errors)))),max_velocity_error_mps=float(max(errors))))
 report=dict(status='translation response fitted; qualification pending',sample_interval_seconds=.015,training_references=list(range(10)),held_out_reference=48,coefficients=coefficients,tests=tests,trials=trials,limits=['Translation-only fit; not a reduced brain','Recorded reference tracking is not joystick control','Turn, climb, disturbance and perch calibration remain required'])
 Path(__file__).with_name('fast-calibration-evaluation.json').write_text(json.dumps(report,indent=2));print(json.dumps({k:v for k,v in report.items() if k!='trials'}),flush=True)
if __name__=='__main__':main()
