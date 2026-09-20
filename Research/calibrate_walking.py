"""Measure loaded physical steering. Held-out errors determine deployment."""
import json,time,subprocess,sys
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor,as_completed
import numpy as np

def trial(settings):
    from walking_backend import WalkingBody
    speed,turn=settings;body=WalkingBody();headings=[];velocities=[]
    try:
        for _ in range(60):
            body.step(np.clip([speed*(1+.4*turn),speed*(1-.4*turn)],0,1.5))
            f=body.obs['fly_orientation'];headings.append(-float(np.arctan2(f[1],f[0])))
            velocities.append(float(np.linalg.norm(body.obs['fly'][1][:2])))
        rates=np.diff(np.unwrap(headings))/.015
        return dict(drive=speed,turn=turn,yaw_rate_radps=float(np.mean(rates[-30:])),
            yaw_rate_std=float(np.std(rates[-30:])),speed_mmps=float(np.mean(velocities[-30:])),
            finite=bool(np.isfinite(body.d.qpos).all()),ended=body.ended)
    finally:body.close()

def features(s,t):return np.asarray([1,s,s*s,s*t,s*s*t,s*t*t*t])

def isolated_trial(settings):
    result=subprocess.run([sys.executable,__file__,'--trial',*[str(v) for v in settings]],
        capture_output=True,text=True,check=True)
    return json.loads(result.stdout)

def main():
    started=time.perf_counter();train=[(s,t) for s in (.5,.9) for t in (-.7,-.35,0,.35,.7)]
    held=[(.7,t) for t in (-.5,-.2,.2,.5)];results={}
    with ThreadPoolExecutor(max_workers=2) as pool:
        jobs={pool.submit(isolated_trial,c):c for c in train+held}
        for job in as_completed(jobs):
            result=job.result();results[jobs[job]]=result;print(json.dumps(result),flush=True)
    coefficients=np.linalg.lstsq(np.stack([features(*c) for c in train]),
        np.asarray([results[c]['yaw_rate_radps'] for c in train]),rcond=None)[0]
    errors=[]
    for c in held:
        predicted=float(features(*c)@coefficients);results[c]['predicted_rate_radps']=predicted
        results[c]['error_radps']=predicted-results[c]['yaw_rate_radps'];errors.append(results[c]['error_radps'])
    rms=float(np.sqrt(np.mean(np.square(errors))));maximum=float(np.max(np.abs(errors)))
    monotonic=all(s*coefficients[3]+s*s*coefficients[4]+3*s*coefficients[5]*t*t>0
        for s in np.linspace(.5,.9,9) for t in np.linspace(-.7,.7,15))
    passed=bool(rms<=.75 and maximum<=1.25 and monotonic and all(r['finite'] and not r['ended'] for r in results.values()))
    report=dict(coefficients=coefficients.tolist(),features=['1','drive','drive^2','drive*turn','drive^2*turn','drive*turn^3'],
        training=[results[c] for c in train],held_out=[results[c] for c in held],
        held_out_rms_radps=rms,held_out_max_radps=maximum,monotonic=monotonic,qualified=passed,
        limits=dict(rms_radps=.75,max_radps=1.25,drive_range=[.5,.9],turn_range=[-.7,.7]),
        rider_mass_fraction=1/3,wall_seconds=time.perf_counter()-started,biologically_validated=False,
        scope='Loaded gait response only; not biological DN decoding or sensory encoding')
    Path(__file__).with_name('walking-calibration.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(dict(qualified=passed,rms=rms,maximum=maximum,monotonic=monotonic)),flush=True)

if __name__=='__main__':
    if len(sys.argv)>1 and sys.argv[1]=='--trial':print(json.dumps(trial(tuple(float(v) for v in sys.argv[2:]))))
    else:main()
