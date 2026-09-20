"""Stable empirical translation surrogate fitted to loaded wing-resolved flight."""
import json,time
from pathlib import Path
import numpy as np

class AveragedTranslation:
 def __init__(self,coefficients,velocity=(0,0,0),target=(0,0,0)):
  self.coefficients=np.asarray(coefficients);self.velocity=np.asarray(velocity,dtype=float).copy();self.target=np.asarray(target,dtype=float).copy()
  if self.coefficients.shape!=(7,3) or not np.isfinite(self.coefficients).all():raise ValueError('Invalid fit')
  if max(abs(np.linalg.eigvals(np.eye(3)-self.coefficients[:3].T)))>=1:raise ValueError('Unstable response')
 def step(self,target):
  target=np.asarray(target,dtype=float)
  if target.shape!=(3,) or not np.isfinite(target).all():raise ValueError('Invalid target')
  self.velocity+=np.r_[self.target-self.velocity,target-self.target,1]@self.coefficients
  self.target=target.copy();return self.velocity.copy()

def fit():
 path=Path(__file__).with_name('fast-calibration-evaluation.json');report=json.loads(path.read_text());x=[];y=[]
 for trial in report['trials'][:-1]:
  for a,b in zip(trial['samples'],trial['samples'][1:]):
   v=np.array(a['v']);u=np.array(a['target']);x.append(np.r_[u-v,np.array(b['target'])-u,1]);y.append(np.array(b['v'])-v)
 coefficients=np.linalg.lstsq(x,y,rcond=None)[0];tests=[]
 for trial in report['trials']:
  samples=trial['samples'];model=AveragedTranslation(coefficients,samples[0]['v'],samples[0]['target']);errors=[];start=time.perf_counter()
  for a,b in zip(samples,samples[1:]):errors.append(np.linalg.norm(model.step(b['target'])-np.array(b['v'])))
  tests.append(dict(index=trial['index'],held_out=trial['index']==48,velocity_rmse_mps=float(np.sqrt(np.mean(np.square(errors)))),maximum_error_mps=float(max(errors)),wall_seconds=time.perf_counter()-start))
 held=tests[-1];qualified=held['velocity_rmse_mps']<=.02 and held['maximum_error_mps']<=.05
 r=dict(status='bounded translation qualification passed' if qualified else 'qualification failed',qualified_translation=qualified,coefficients=coefficients.tolist(),sample_interval_seconds=.015,poles=[dict(real=float(z.real),imag=float(z.imag)) for z in np.linalg.eigvals(np.eye(3)-coefficients[:3].T)],tests=tests,rider_mass_fraction=1/3,training_references=list(range(10)),held_out_reference=48,limits=['Empirical translation only; not a reduced neural network','Yaw/joystick/landing behavior is an engineering controller, not calibrated biology','Qualification covers the recorded references, not unrestricted flight'])
 Path(__file__).with_name('averaged-flight-calibration.json').write_text(json.dumps(r,indent=2));print(json.dumps(r),flush=True)
if __name__=='__main__':fit()
