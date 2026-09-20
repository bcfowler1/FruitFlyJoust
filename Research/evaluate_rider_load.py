"""Bounded loaded-flight test; does not presume payload flight succeeds."""
import json
from pathlib import Path
from evaluate_trained_flight import tf, ASSETS, rollout
from optimized_flight_policy import CompiledMeanPolicy

def main():
    policy=CompiledMeanPolicy(tf.saved_model.load(str(ASSETS/'flight')))
    trials=[]
    for index in [0,1,48]:
        result=rollout(policy,index,max_steps=4000,render=False,rider_fraction=1/3)
        result.pop('final_qpos')
        trials.append(result)
        print('LOADED_FLIGHT',index,result['simulated_seconds'],result['reference_end'],flush=True)
    report=dict(status='references completed' if all(t['reference_end'] for t in trials) else 'loaded flight requires controller adaptation',
        trials=trials,biologically_validated=False, mass_fraction=1/3,
        limitations='Rigid spherical payload. Unchanged unloaded checkpoint. Short recorded references only.')
    Path(__file__).with_name('rider-load-evaluation.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report),flush=True)

if __name__=='__main__': main()
