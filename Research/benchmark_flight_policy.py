import json,time
from pathlib import Path
from evaluate_trained_flight import ASSETS,tf,np,flight_imitation,rollout
from optimized_flight_policy import CompiledMeanPolicy

def main():
    restored=tf.saved_model.load(str(ASSETS/'flight'))
    compiled=CompiledMeanPolicy(restored)
    env=flight_imitation(ref_path=str(ASSETS/'flight-dataset_saccade-evasion_augmented.hdf5'),
        wpg_pattern_path=str(ASSETS/'wing_pattern_fmech.npy'),randomize_start_step=False,
        traj_indices=[48],random_state=np.random.RandomState(42))
    obs={k:tf.convert_to_tensor(v[None],dtype=tf.float32) for k,v in env.reset().observation.items()}
    expected=np.asarray(restored(obs).mean()); actual=np.asarray(compiled(obs).mean())
    parity=bool(np.allclose(expected,actual,rtol=1e-6,atol=1e-7))
    if not parity: raise RuntimeError('Compiled policy changed actions')
    timings={}
    for name,policy in [('original',restored),('compiled',compiled)]:
        start=time.perf_counter()
        for _ in range(1000): np.asarray(policy(obs).mean())
        timings[name]=(time.perf_counter()-start)/1000
    env.close()
    trial=rollout(compiled,48,max_steps=4000,render=False)
    trial.pop('final_qpos')
    report=dict(action_parity=parity,inference_seconds_per_call=timings,
        inference_speedup=timings['original']/timings['compiled'],rollout=trial,
        whole_body_realtime_ratio=trial['simulated_seconds']/trial['wall_seconds'],
        neural_coupling=False,checkpoint_unchanged=True)
    Path(__file__).with_name('flight-performance-evaluation.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report),flush=True)

if __name__=='__main__': main()
