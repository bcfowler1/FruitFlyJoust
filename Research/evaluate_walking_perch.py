"""No root reset between loaded walking, gripping, airborne and landing phases."""
import json,time,sys,subprocess
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import numpy as np
from walking_backend import WalkingBody
from walking_perch import WalkingPerch

def trial(surface):
    b=WalkingBody();c=WalkingPerch(b);events=[];begin=time.perf_counter()
    try:
        for _ in range(30):c.step([.7,.7])
        c.land()
        for _ in range(100):
            c.step([0,0])
            if c.phase=='perched':break
        assert c.phase=='perched'
        b.m.opt.gravity[:]=dict(floor=(0,0,-9810),wall=(-9810,0,0),ceiling=(0,0,9810))[surface]
        reference=b.d.qpos[:3].copy();minimum=6;slip=0.
        for _ in range(30):
            c.step([0,0]);minimum=min(minimum,len(b.contacts()));slip=max(slip,float(np.linalg.norm(b.d.qpos[:3]-reference)))
        held=minimum>=3 and slip<1
        events.append(dict(phase='hold',contacts=len(b.contacts()),minimum_contacts=minimum,slip_mm=slip,passed=held))
        for cycle in range(2):
            before=b.d.qpos.copy();c.launch();assert np.array_equal(before,b.d.qpos)
            for _ in range(100):
                c.step([0,0])
                if c.phase=='flying':break
            launched=c.phase=='flying' and len(b.contacts())==0
            for _ in range(20):c.step([0,0])
            before=b.d.qpos.copy();c.land();assert np.array_equal(before,b.d.qpos)
            for _ in range(150):
                c.step([0,0])
                if c.phase=='perched':break
            landed=c.phase=='perched';minimum=6;reference=b.d.qpos[:3].copy();slip=0
            for _ in range(30):
                c.step([0,0]);minimum=min(minimum,len(b.contacts()));slip=max(slip,float(np.linalg.norm(b.d.qpos[:3]-reference)))
            stable=bool(minimum>=3 and slip<1 and np.linalg.norm(b.d.qvel[:3])<2)
            events.append(dict(cycle=cycle,launched=launched,landed=landed,held=stable,minimum_contacts=minimum,slip_mm=slip,
                assistance_off=bool(np.all(b.d.xfrc_applied==0) and np.all(b.d.qfrc_applied==0))))
        c.resume_walk();resumed=c.phase=='walking'
        if resumed:
            for _ in range(20):c.step([.7,.7])
        return dict(surface=surface,events=events,resumed_walk=resumed and not b.ended,finite=bool(np.isfinite(b.d.qpos).all()),
            passed=held and all(e.get('launched',True) and e.get('landed',True) and e.get('held',True) for e in events),
            wall_seconds=time.perf_counter()-begin,rider_mass_fraction=1/3,root_snapping=False,
            limits='One flat surface; static surface-relative gravity; engineering hover force and fixed airborne leg targets; not wing or neural landing control')
    finally:b.close()

def main():
    results=[]
    def isolated(surface):
        r=subprocess.run([sys.executable,__file__,'--surface',surface],capture_output=True,text=True,check=True)
        result=json.loads(r.stdout);print(json.dumps(result),flush=True);return result
    with ThreadPoolExecutor(max_workers=2) as pool:results=list(pool.map(isolated,('floor','wall','ceiling')))
    Path(__file__).with_name('walking-perch-evaluation.json').write_text(json.dumps(dict(trials=results),indent=2))

if __name__=='__main__':
    if len(sys.argv)>1:print(json.dumps(trial(sys.argv[2])))
    else:main()
