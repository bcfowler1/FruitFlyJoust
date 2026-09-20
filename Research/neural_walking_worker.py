"""Experimental DNa01/DNa02 spike interface; not a reproduction of Eon's mappings."""
import csv,json,sys,math
from pathlib import Path
from brain_body_interface import NeuralPopulation,population_rate_hz
from gpu_backend import NeuralRuntime,ROOT

def main():
    import argparse
    parser=argparse.ArgumentParser()
    parser.add_argument('--diagnostic-isolate',action='store_true',help='Disable synaptic weights in memory for a diagnostic comparison; not gameplay')
    parser.add_argument('--diagnostic-pfl3',action='store_true',help='Stimulate anatomically connected upstream PFL3 cells instead of DNa01/02; not gameplay')
    args=parser.parse_args()
    annotation=ROOT/'work/research/flywire-annotations-8587524.tsv'
    rows=list(csv.DictReader(annotation.open(),delimiter='\t'))
    source='flyconnectome/flywire_annotations 8587524c1748ce5ef2080822a2fc890fc03bf597; v783'
    populations={side:NeuralPopulation('DNa01_DNa02_'+side,tuple(r['root_id'] for r in rows
        if r['cell_type'] in ('DNa01','DNa02') and r['side']==side),source) for side in ('left','right')}
    stimulus_names={side:side for side in ('left','right')}
    if args.diagnostic_pfl3:
        import pyarrow.parquet as pq
        dn={int(r['root_id']):r['side'] for r in rows if r['cell_type']=='DNa02'}
        pfl={int(r['root_id']) for r in rows if r['cell_type']=='PFL3'}
        edges=pq.read_table(ROOT/'work/research/fly-brain/data/2025_Connectivity_783.parquet',
            filters=[('Postsynaptic_ID','in',list(dn)),('Presynaptic_ID','in',list(pfl))],
            columns=['Presynaptic_ID','Postsynaptic_ID','Connectivity','Excitatory']).to_pylist()
        scores={root:{'left':0,'right':0} for root in pfl}
        for edge in edges:
            if edge['Excitatory']>0:scores[edge['Presynaptic_ID']][dn[edge['Postsynaptic_ID']]]+=edge['Connectivity']
        for side,other in (('left','right'),('right','left')):
            stimulus_names[side]='stim_'+side
            # Paper's PFL3L/R groups are defined by descending output side,
            # not by assuming the annotation soma hemisphere identifies axon side.
            ids=tuple(str(root) for root in sorted(pfl) if scores[root][side]>scores[root][other])
            populations['stim_'+side]=NeuralPopulation('PFL3_to_DNa02_'+side,ids,source)
    output=sys.stdout;sys.stdout=sys.stderr
    brain=NeuralRuntime(populations=populations,connectivity_scale=0 if args.diagnostic_isolate else 1);brain.rates.zero_();brain.enable_graph(150);brain.reset()
    previous=brain.population_summary()
    print(json.dumps(dict(ready=True,populations={k:list(v.root_ids) for k,v in populations.items()})),file=output,flush=True)
    for line in sys.stdin:
        request=json.loads(line)
        if request.get('stop'):break
        if request.get('reset'):
            brain.reset();previous=brain.population_summary()
            print(json.dumps(dict(reset=True,neural_state_reset=True)),file=output,flush=True);continue
        turn=float(request['turn']);speed=float(request['speed_mmps'])
        yaw=float(request.get('yaw_rate_radps',0));contacts=request.get('contacting_claws',6)
        if (not -1<=turn<=1 or not 0<=speed<1000 or not math.isfinite(yaw) or abs(yaw)>1000
            or type(contacts) is not int or not 0<=contacts<=6):raise ValueError('Invalid walking feedback')
        brain.rates.zero_()
        # Diagnostic engineering encoder: actual body speed modulates excitation;
        # joystick cues never stand in for recorded biological sensory signals.
        base=max(0,180-4*speed)
        correction=max(-1,min(1,turn-.15*yaw)) if contacts>=3 else 0.
        for side,sign in (('left',-1),('right',1)):
            brain.rates[:,brain.monitored[stimulus_names[side]]]=max(0,min(400,base+sign*100*correction))
        brain.step(150);brain.synchronize();counted=brain.population_summary()
        rates={side:population_rate_hz([b-a for a,b in zip(previous[side]['spike_counts'],counted[side]['spike_counts'])],.015) for side in populations}
        previous=counted
        stats=brain.summary()
        stimulated=brain.torch.unique(brain.torch.cat([brain.monitored[name] for name in stimulus_names.values()]))
        stats['downstream_active_neurons']=int(brain.active.sum().item()-brain.active[stimulated].sum().item())
        print(json.dumps(dict(neural_time=brain.simulated_ms/1000,rates=rates,stats=stats,
            feedback=dict(speed_mmps=speed,yaw_rate_radps=yaw,contacting_claws=contacts),
            encoder='synthetic PFL3 upstream excitation; anatomical output-side grouping; unvalidated' if args.diagnostic_pfl3 else 'synthetic direct descending excitation with measured speed/yaw/contact feedback; unvalidated')),
            file=output,flush=True)

if __name__=='__main__':main()
