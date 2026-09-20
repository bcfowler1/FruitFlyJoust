"""Separate CUDA process, JSON-lines IPC, real full-connectome spike readout."""
import json, sys
from pathlib import Path
from brain_body_interface import NeuralPopulation, RiderCues, population_rate_hz
from experimental_coupling import ExperimentalEncoder, BodyFeedback, decode_rates
from gpu_backend import NeuralRuntime

def main():
    meta=json.loads(Path(__file__).with_name('flight-populations.json').read_text())
    populations={side:NeuralPopulation('DNg02_'+side,tuple(ids),meta['annotation_source']) for side,ids in meta['populations'].items()}
    # Upstream diagnostic output is redirected so stdout remains a strict IPC channel.
    output=sys.stdout; sys.stdout=sys.stderr
    runtime=NeuralRuntime(populations=populations)
    runtime.rates.zero_(); runtime.enable_graph(150)
    origin=runtime.simulated_ms; encoder=ExperimentalEncoder()
    previous=runtime.population_summary()
    print(json.dumps(dict(ready=True, experimental=True)),file=output,flush=True)
    for line in sys.stdin:
        request=json.loads(line)
        if request.get('stop'): break
        if request.get('reset'):
            runtime.reset(); encoder=ExperimentalEncoder(); origin=runtime.simulated_ms
            previous=runtime.population_summary()
            print(json.dumps(dict(reset=True,neural_state_reset=True)),file=output,flush=True); continue
        cues=RiderCues(**request['cues']); feedback=BodyFeedback(**request['feedback'])
        steps=int(request.get('steps',150))
        if not 1 <= steps <= 150: raise ValueError('Invalid neural window')
        window=steps*runtime.source.DT/1000
        excitation=encoder.encode(cues,feedback,window)
        runtime.rates.zero_()
        for side,indices in runtime.monitored.items(): runtime.rates[:,indices]=excitation[side]
        if steps == 150: runtime.step(150)
        else:
            graph=runtime.graph; persistent=runtime.state; runtime.graph=None
            runtime.step(steps)
            for destination, final in zip(persistent,runtime.state): destination.copy_(final)
            runtime.state=persistent; runtime.graph=graph
        runtime.synchronize()
        counted=runtime.population_summary(); rates={}
        for side in populations:
            counts=[b-a for a,b in zip(previous[side]['spike_counts'],counted[side]['spike_counts'])]
            rates[side]=population_rate_hz(counts,window)
        previous=counted
        result=dict(neural_time=(runtime.simulated_ms-origin)/1000,rates=rates,
            residual=decode_rates(rates),excitation_hz=excitation,stats=runtime.summary(),experimental=True,
            encoder='synthetic feedback controller; direct descending excitation; not biological sensory encoding')
        print(json.dumps(result),file=output,flush=True)

if __name__=='__main__': main()
