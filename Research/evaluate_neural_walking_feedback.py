"""Engineering qualification of existing connectome feedback and gait interface.

Inputs are synthetic controlled feedback, not recorded sensory neurons. No new
motor-neuron mapping or biological validation is implied by a passing report.
"""
import json,time
from pathlib import Path
import numpy as np
from neural_flight_client import NeuralFlightClient
from walking_steering import WalkingSteering

def main():
    started=time.perf_counter();rows=[]
    cases=[('neutral',0,0,0,6),('left',-.5,0,0,6),('right',.5,0,0,6),
        ('faster',0,20,0,6),('yaw_feedback',0,0,.8,6),('low_contact',.5,0,0,2)]
    brain=NeuralFlightClient('neural_walking_worker.py','neural-feedback-worker.log')
    try:
        for name,turn,speed,yaw,contacts in cases:
            brain.reset();controller=WalkingSteering();samples=[]
            for step in range(16):
                frame=brain.send(dict(turn=turn,speed_mmps=speed,yaw_rate_radps=yaw,contacting_claws=contacts))
                cue=controller.neural_cue(frame['rates'],turn,1)
                action,state=controller.update(.7,cue,0)
                assert frame['stats']['neurons']==138639
                assert frame['stats']['finite_membrane_state']
                assert abs(frame['neural_time']-(step+1)*.015)<1e-8
                assert np.isfinite(action).all() and np.all(action>=0) and np.all(action<=1.5)
                samples.append(dict(rates=frame['rates'],neural_turn=cue,gait_drive=action.tolist()))
            rates={side:float(np.mean([s['rates'][side] for s in samples])) for side in ('left','right')}
            rows.append(dict(case=name,inputs=dict(turn=turn,speed_mmps=speed,yaw_rate_radps=yaw,contacting_claws=contacts),
                mean_rates_hz=rates,mean_neural_turn=float(np.mean([s['neural_turn'] for s in samples])),
                final_stats=frame['stats'],samples=samples))
            print(json.dumps(dict(completed=name,rates=rates)),flush=True)
        by_name={r['case']:r for r in rows}
        gates=dict(left_right_signed_output=by_name['left']['mean_neural_turn']<0<by_name['right']['mean_neural_turn'],
            speed_reduces_excited_population_rate=sum(by_name['faster']['mean_rates_hz'].values())<sum(by_name['neutral']['mean_rates_hz'].values()),
            yaw_feedback_opposes_positive_yaw=by_name['yaw_feedback']['mean_neural_turn']<by_name['neutral']['mean_neural_turn'],
            low_contact_suppresses_turn_excitation=by_name['low_contact']['samples']==by_name['neutral']['samples'],
            downstream_connectome_activity=all(r['final_stats']['downstream_active_neurons']>0 for r in rows))
        result=dict(qualified=all(gates.values()),gates=gates,neurons=138639,rows=rows,wall_seconds=time.perf_counter()-started,
            limitations='Controlled synthetic feedback and direct DNa01/DNa02 excitation. Engineering rate-to-gait steering, not brain-to-leg motor decoding, real sensory encoding or biological qualification. Body trajectory is not tested here.')
        Path(__file__).with_name('neural-walking-feedback-evaluation.json').write_text(json.dumps(result,indent=2))
        print(json.dumps(dict(qualified=result['qualified'],gates=gates)),flush=True)
    finally:brain.close()

if __name__=='__main__':main()
