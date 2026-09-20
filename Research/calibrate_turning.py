"""Measure loaded recorded-reference yaw response; never equate reins with neurons."""
import json,time
from pathlib import Path
import numpy as np
import mujoco
from serve_flight import create_environment,ASSETS,tf
from optimized_flight_policy import CompiledMeanPolicy
from optimized_environment import cache_actuator_mapping
from rider_load import RiderLoad

def heading(q):
 w,x,y,z=q
 return np.arctan2(2*(w*z+x*y),1-2*(y*y+z*z))

def collect(policy,index):
 env=cache_actuator_mapping(create_environment(index));ts=env.reset();load=RiderLoad(env.physics).apply(env.physics)
 spec=env.action_spec();samples=[];window=[];clips=0;start=time.perf_counter()
 anchor=mujoco.mj_name2id(env.physics.model.ptr,mujoco.mjtObj.mjOBJ_BODY,'walker/thorax')
 previous=heading(env.physics.data.qpos[3:7]);elapsed=0.;reference_previous=heading(env.task._ref_qpos[0,3:7]);reference_elapsed=0.
 while True:
  obs={k:tf.convert_to_tensor(v[None],dtype=tf.float32) for k,v in ts.observation.items()}
  action=np.asarray(policy(obs).mean())[0];clips+=int(np.any((action<spec.minimum)|(action>spec.maximum)))
  ts=env.step(np.clip(action,spec.minimum,spec.maximum).copy())
  actual=heading(env.physics.data.qpos[3:7]);delta=np.arctan2(np.sin(actual-previous),np.cos(actual-previous));previous=actual;elapsed+=delta
  step=round(float(env.physics.data.time)/.0002);ref=min(step,len(env.task._ref_qpos)-1)
  desired=heading(env.task._ref_qpos[ref,3:7]);change=np.arctan2(np.sin(desired-reference_previous),np.cos(desired-reference_previous));reference_previous=desired;reference_elapsed+=change
  window.append((delta/.0002,change/.0002))
  if step%75==0:
   rate,target=np.mean(window,axis=0);samples.append(dict(t=float(env.physics.data.time),rate=float(rate),target=float(target),heading_change=float(elapsed),reference_heading_change=float(reference_elapsed)));window=[]
  if ts.last():break
 result=dict(index=index,reference_completed=bool(env.task._reached_traj_end),clips=clips,wall_seconds=time.perf_counter()-start,rider_mass_mg=load['rider_mass_mg'],samples=samples)
 env.close();print(json.dumps({k:v for k,v in result.items() if k!='samples'}),flush=True);return result

def main():
 policy=CompiledMeanPolicy(tf.saved_model.load(str(ASSETS/'flight')))
 trials=[collect(policy,i) for i in (0,1,2,3,4,5,6,7,8,9,48)]
 x=[];y=[]
 for trial in trials[:-1]:
  if trial['clips'] or not trial['reference_completed']:continue
  for a,b in zip(trial['samples'],trial['samples'][1:]):
   x.append([a['target']-a['rate'],b['target']-a['target'],1]);y.append(b['rate']-a['rate'])
 coefficients=np.linalg.lstsq(x,y,rcond=None)[0];pole=1-float(coefficients[0]);tests=[]
 for trial in trials:
  samples=trial['samples'];rate=samples[0]['rate'];errors=[]
  for a,b in zip(samples,samples[1:]):
   rate+=np.dot([a['target']-rate,b['target']-a['target'],1],coefficients);errors.append(rate-b['rate'])
  tests.append(dict(index=trial['index'],held_out=trial['index']==48,rate_rmse_rad_s=float(np.sqrt(np.mean(np.square(errors)))),maximum_rate_error_rad_s=float(max(abs(np.asarray(errors))))))
 qualified=abs(pole)<1 and tests[-1]['rate_rmse_rad_s']<=1 and tests[-1]['maximum_rate_error_rad_s']<=3
 report=dict(status='bounded yaw qualification passed' if qualified else 'rejected: yaw response exceeds engineering error limits',qualified_yaw=qualified,tolerance_rmse_rad_s=1,tolerance_max_rad_s=3,training_references=[t['index'] for t in trials[:-1] if not t['clips'] and t['reference_completed']],stable=abs(pole)<1,pole=pole,coefficients=coefficients.tolist(),sample_interval_seconds=.015,tests=tests,trials=trials,limits=['Recorded-reference heading response only; no calibrated joystick or neural mapping','Yaw projection is poorly conditioned near vertical attitudes','Wing dynamics and contact transitions remain separate'])
 Path(__file__).with_name('turning-calibration-evaluation.json').write_text(json.dumps(report,indent=2));print(json.dumps({k:v for k,v in report.items() if k!='trials'}),flush=True)
if __name__=='__main__':main()
