"""Full reference completion and declared disturbance checks, not biological validation."""
import json
from pathlib import Path
from evaluate_trained_flight import ASSETS, tf, rollout

def main():
    policy=tf.saved_model.load(str(ASSETS/'flight'))
    trials=[]
    # Include the longest available reference and a range of trajectory lengths.
    for index in [0,1,2,18,30,48,57,59,78,85]:
        result=rollout(policy,index,max_steps=4000,render=False)
        result.pop('final_qpos')
        result['lateral_velocity_impulse_mps']=0
        trials.append(result)
        print('FULL_REFERENCE',index,result['simulated_seconds'],result['reference_end'],flush=True)
    for impulse in [.02,.05]:
        result=rollout(policy,48,max_steps=4000,render=False,velocity_impulse_mps=impulse)
        result.pop('final_qpos'); result['lateral_velocity_impulse_mps']=impulse
        trials.append(result)
        print('DISTURBANCE',impulse,result['reference_end'],flush=True)
    failures=[t for t in trials if not t['reference_end']]
    report=dict(status='all references completed' if not failures else 'qualified results: some references terminated early',
        neural_coupling=False,biological_validation=False,trials=trials,
        completed=sum(t['reference_end'] for t in trials),total=len(trials),
        failure_trajectories=[t['trajectory'] for t in failures],
        limitation='Recorded paths are shorter than one second; this does not establish sustained free flight.')
    Path(__file__).with_name('extended-flight-evaluation.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report),flush=True)

if __name__=='__main__': main()
