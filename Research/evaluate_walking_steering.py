import json,sys,subprocess
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
import numpy as np

def trial(speed,turn,head=False):
    from walking_backend import WalkingBody
    from walking_steering import WalkingSteering
    b=WalkingBody(head_stabilization=head);c=WalkingSteering();headings=[];goal=None
    try:
        for _ in range(80):
            action,state=c.update(speed,turn,b.feedback()['yaw_rate_radps']);goal=state['target_yaw_radps']
            b.step(action);f=b.obs['fly_orientation'];headings.append(-float(np.arctan2(f[1],f[0])))
        measured=float(np.mean(np.diff(np.unwrap(headings))[-30:])/.015)
        return dict(drive=speed,cue=turn,target_yaw_radps=goal,measured_yaw_radps=measured,error_radps=measured-goal,
            finite=bool(np.isfinite(b.d.qpos).all()),ended=b.ended)
    finally:b.close()

def isolated(settings):
    speed,turn,head=settings
    r=subprocess.run([sys.executable,__file__,'--trial',str(speed),str(turn)]+(['--head'] if head else []),capture_output=True,text=True,check=True)
    result=json.loads(r.stdout);print(json.dumps(result),flush=True);return result

def main(head=False):
    with ThreadPoolExecutor(max_workers=2) as pool:results=list(pool.map(isolated,[(s,t,head) for s in (.5,.7,.9) for t in (-.5,.5)]))
    rms=float(np.sqrt(np.mean([r['error_radps']**2 for r in results])));maximum=max(abs(r['error_radps']) for r in results)
    report=dict(trials=results,rms_radps=rms,max_radps=maximum,limits=dict(rms_radps=.35,max_radps=.5),
        qualified=rms<=.35 and maximum<=.5 and all(r['finite'] and not r['ended'] for r in results),trained_head_control=head,biologically_validated=False)
    Path(__file__).with_name('head-walking-controller-evaluation.json' if head else 'walking-controller-evaluation.json').write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)

if __name__=='__main__':
    import argparse
    parser=argparse.ArgumentParser();parser.add_argument('--head',action='store_true');parser.add_argument('--trial',nargs=2,type=float)
    args=parser.parse_args()
    if args.trial:print(json.dumps(trial(*args.trial,head=args.head)))
    else:main(args.head)
