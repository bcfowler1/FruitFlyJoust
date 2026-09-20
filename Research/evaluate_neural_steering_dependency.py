"""Diagnostic full-network versus isolated-cell dependence of steering readout."""
import json,time
from pathlib import Path
from neural_flight_client import NeuralFlightClient

def main(upstream=False):
    started=time.perf_counter();results={}
    for mode,args in [('connected',()),('isolated',('--diagnostic-isolate',))]:
        brain=NeuralFlightClient('neural_walking_worker.py','neural-dependency-'+mode+'.log',args+(('--diagnostic-pfl3',) if upstream else ()))
        try:
            trials=[]
            for turn in (-.5,0,.5):
                brain.reset();samples=[]
                for step in range(32):
                    f=brain.send(dict(turn=turn,speed_mmps=0,yaw_rate_radps=0,contacting_claws=6))
                    assert f['stats']['neurons']==138639 and f['stats']['finite_membrane_state']
                    samples.append(f['rates'])
                trials.append(dict(turn=turn,rates=samples,stats=f['stats']))
            results[mode]=trials
            print(json.dumps(dict(completed=mode)),flush=True)
        finally:brain.close()
    max_difference=max(abs(a['rates'][i][side]-b['rates'][i][side])
        for a,b in zip(results['connected'],results['isolated']) for i in range(32) for side in ('left','right'))
    report=dict(comparison_completed=True,upstream_pfl3=upstream,network_changes_current_steering_readout=max_difference>0,
        maximum_rate_difference_hz=max_difference,results=results,wall_seconds=time.perf_counter()-started,
        limitations='Diagnostic synaptic ablation in device memory only. Identical reset/seed/input. PFL3 mode stimulates anatomically grouped upstream cells; default directly excites DNa01/DNa02. No biological sensory or brain-to-leg validation. No production weights or gameplay settings changed.')
    Path(__file__).with_name('neural-upstream-dependency-evaluation.json' if upstream else 'neural-steering-dependency-evaluation.json').write_text(json.dumps(report,indent=2))
    print(json.dumps({k:v for k,v in report.items() if k!='results'}),flush=True)

if __name__=='__main__':
    import argparse
    p=argparse.ArgumentParser();p.add_argument('--upstream-pfl3',action='store_true');args=p.parse_args()
    main(args.upstream_pfl3)
