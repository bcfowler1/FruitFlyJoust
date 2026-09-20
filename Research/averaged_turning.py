"""Second-order empirical heading response; measured references, not neural decoding."""
import json
from pathlib import Path
import numpy as np

class AveragedHeading:
 def __init__(self,coefficients,heading=0.,target=0.,previous_heading=None,previous_target=None):
  self.c=np.asarray(coefficients,dtype=float)
  if self.c.shape!=(5,) or not np.isfinite(self.c).all():raise ValueError('Invalid yaw fit')
  self.poles=np.linalg.eigvals([[1-self.c[0]+self.c[2],-self.c[2]],[1,0]])
  if max(abs(self.poles))>=1:raise ValueError('Unstable yaw fit')
  self.heading=float(heading);self.target=float(target)
  self.previous_heading=float(heading if previous_heading is None else previous_heading)
  self.previous_target=float(target if previous_target is None else previous_target)
 def step(self,target):
  if not np.isfinite(target):raise ValueError('Invalid heading target')
  delta=np.dot([self.target-self.heading,target-self.target,self.heading-self.previous_heading,self.target-self.previous_target,1],self.c)
  self.previous_heading=self.heading;self.previous_target=self.target;self.heading+=delta;self.target=float(target)
  return self.heading

def main():
 report=json.loads(Path(__file__).with_name('turning-calibration-evaluation.json').read_text());x=[];y=[]
 training=[t for t in report['trials'][:-1] if not t['clips'] and t['reference_completed']]
 for t in training:
  for p,a,b in zip(t['samples'],t['samples'][1:],t['samples'][2:]):
   x.append([a['reference_heading_change']-a['heading_change'],b['reference_heading_change']-a['reference_heading_change'],a['heading_change']-p['heading_change'],a['reference_heading_change']-p['reference_heading_change'],1]);y.append(b['heading_change']-a['heading_change'])
 c=np.linalg.lstsq(x,y,rcond=None)[0];model=AveragedHeading(c)
 # Fresh holdout after selecting model structure using training records only.
 from calibrate_turning import collect
 from serve_flight import ASSETS,tf
 from optimized_flight_policy import CompiledMeanPolicy
 holdout=collect(CompiledMeanPolicy(tf.saved_model.load(str(ASSETS/'flight'))),49)
 tests=[]
 for t in [*training,holdout]:
  s=t['samples'];model=AveragedHeading(c,s[1]['heading_change'],s[1]['reference_heading_change'],s[0]['heading_change'],s[0]['reference_heading_change']);errors=[]
  for b in s[2:]:errors.append(model.step(b['reference_heading_change'])-b['heading_change'])
  tests.append(dict(index=t['index'],held_out=t['index']==49,heading_rmse_rad=float(np.sqrt(np.mean(np.square(errors)))),maximum_heading_error_rad=float(max(abs(np.asarray(errors))))))
 h=tests[-1];qualified=holdout['reference_completed'] and not holdout['clips'] and h['heading_rmse_rad']<=.08 and h['maximum_heading_error_rad']<=.2
 output=dict(status='bounded heading qualification passed' if qualified else 'heading qualification failed',qualified_heading=bool(qualified),coefficients=c.tolist(),poles=[dict(real=float(p.real),imag=float(p.imag)) for p in model.poles],sample_interval_seconds=.015,tests=tests,held_out_reference=49,holdout=holdout,limits=['Recorded-reference heading only; no calibrated joystick or neural mapping','Two measured initial states required','Vertical-attitude heading projection is not qualified','No wing dynamics or collision model in the reduced response'])
 Path(__file__).with_name('averaged-heading-calibration.json').write_text(json.dumps(output,indent=2));print(json.dumps({k:v for k,v in output.items() if k!='holdout'}),flush=True)
if __name__=='__main__':main()
