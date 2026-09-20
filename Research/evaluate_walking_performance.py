"""Exact-state equivalence and measured speed; not a reduced brain/body."""
import json,time
from pathlib import Path
import numpy as np
from walking_backend import WalkingBody

def run(optimized):
    b=WalkingBody(optimized=optimized);states=[];begin=time.perf_counter()
    try:
        for action in ([.7,.7],[.98,.42],[.42,.98]):
            for _ in range(12):
                b.step(action);states.append(np.r_[b.d.qpos,b.d.qvel].copy())
        elapsed=time.perf_counter()-begin
        b.reset();b.step([.7,.7]);reset=np.r_[b.d.qpos,b.d.qvel].copy()
        return np.array(states),reset,elapsed
    finally:b.close()

if __name__=='__main__':
    a,ar,at=run(False);b,br,bt=run(True)
    error=float(np.max(np.abs(a-b)));reset_error=float(np.max(np.abs(ar-br)))
    result=dict(qualified=bool(error<1e-10 and reset_error<1e-10 and np.isfinite(b).all()),
        maximum_state_error=error,reset_state_error=reset_error,reference_wall_seconds=at,
        optimized_wall_seconds=bt,speedup=at/bt,simulated_seconds=.54,
        mechanism='same full physics and gait; reuse duplicate unchanged-time observation',
        biologically_validated=False,reduced_model=False)
    Path(__file__).with_name('walking-performance-evaluation.json').write_text(json.dumps(result,indent=2));print(result)
