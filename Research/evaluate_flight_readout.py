"""Measure identified descending-neuron counts; do not decode them into motor commands."""
import json
from pathlib import Path
from brain_body_interface import NeuralPopulation, population_rate_hz
from gpu_backend import NeuralRuntime

def main():
    metadata=json.loads(Path(__file__).with_name('flight-populations.json').read_text())
    populations={side:NeuralPopulation('DNg02_'+side,tuple(ids),metadata['annotation_source'])
        for side,ids in metadata['populations'].items()}
    runtime=NeuralRuntime(populations=populations)
    runtime.step(150,rate_hz=0)
    before=runtime.population_summary()
    runtime.enable_graph(150)
    runtime.step(1500,rate_hz=200)
    after=runtime.population_summary()
    result={}
    for name in populations:
        counts=[b-a for a,b in zip(before[name]['spike_counts'],after[name]['spike_counts'])]
        window=after[name]['window_seconds']-before[name]['window_seconds']
        result[name]=dict(spike_counts=counts,window_seconds=window,mean_rate_hz=population_rate_hz(counts,window))
    # Artificial direct excitation is a counting diagnostic, not a biological sensory encoder.
    count_before=runtime.population_summary()
    runtime.rates.zero_()
    for indices in runtime.monitored.values(): runtime.rates[:,indices]=200
    runtime.step(1500)
    count_after=runtime.population_summary()
    direct={name:sum(count_after[name]['spike_counts'])-sum(count_before[name]['spike_counts']) for name in populations}
    if not all(value>0 for value in direct.values()): raise RuntimeError('Population monitor failed direct-excitation diagnostic')
    report=dict(status='readout executed',full_network=runtime.summary(),populations=result,
        diagnostic='sugar sensory stimulus, not flight sensory input',direct_excitation_counting_check=direct,decoder_enabled=False,
        limitation='Readout execution does not establish flight activation, calibration, or neural control.')
    Path(__file__).with_name('flight-readout-evaluation.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report),flush=True)

if __name__=='__main__': main()
