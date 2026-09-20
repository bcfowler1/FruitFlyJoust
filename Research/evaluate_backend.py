"""Independent smoke tests, not an integrated or biologically validated controller."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import time
import traceback

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(__file__).resolve().parent
RESEARCH = ROOT / 'work' / 'research'

def revision(path):
    return subprocess.check_output(['git', '-C', str(path), 'rev-parse', 'HEAD'], text=True).strip()

def body_test():
    import numpy as np
    import mujoco
    repo = RESEARCH / 'flybody'
    xml = repo / 'flybody' / 'fruitfly' / 'assets' / 'floor.xml'
    start = time.perf_counter()
    model = mujoco.MjModel.from_xml_path(str(xml))
    data = mujoco.MjData(model)
    mujoco.mj_forward(model, data)
    # Zero-actuation finite-state check, not a walking or flight policy.
    for _ in range(100):
        mujoco.mj_step(model, data)
    assert np.isfinite(data.qpos).all() and np.isfinite(data.qvel).all()
    result = dict(revision=revision(repo), bodies=model.nbody, joints=model.njnt,
        actuators=model.nu, timestep_seconds=model.opt.timestep,
        simulated_seconds=float(data.time), elapsed_seconds=time.perf_counter() - start,
        stable_finite_state=True, controller='zero actuation; no learned policy')
    try:
        from PIL import Image
        camera = mujoco.MjvCamera()
        mujoco.mjv_defaultCamera(camera)
        camera.lookat[:] = data.subtree_com[1]
        camera.distance = 1.1
        camera.azimuth = 125
        camera.elevation = -22
        with mujoco.Renderer(model, height=640, width=960) as renderer:
            renderer.update_scene(data, camera=camera)
            Image.fromarray(renderer.render()).save(OUT / 'body-preview.png')
        result['preview'] = 'body-preview.png'
    except Exception as exc:
        result['preview_error'] = str(exc)
    return result

def brain_test():
    import numpy as np
    import pandas as pd
    import os
    import tempfile
    from unittest.mock import patch
    cache = ROOT / 'work' / 'research-cache'
    cache.mkdir(exist_ok=True)
    tempfile.tempdir = str(cache)
    original_expanduser = os.path.expanduser
    def cache_user_path(path):
        path = os.fspath(path)
        return str(cache / path[2:]) if path.startswith('~/') or path.startswith('~\\') else (
            str(cache) if path == '~' else original_expanduser(path))
    # Brian2 imports create ~/.brian on Windows. Keep research caches entirely
    # within the workspace without changing HOME or installing globally.
    with patch('os.path.expanduser', side_effect=cache_user_path):
        import brian2 as b
    repo = RESEARCH / 'fly-brain'
    upstream = repo / 'code' / 'paper-phil-drosophila' / 'model.py'
    sys.path.insert(0, str(repo / 'code'))
    from benchmark import get_experiment
    spec = importlib.util.spec_from_file_location('upstream_lif', upstream)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    b.start_scope()
    b.prefs.codegen.target = 'numpy'
    b.defaultclock.dt = .1 * b.ms
    b.seed(42)
    params = dict(module.default_params)
    # The upstream reset assigns w, which is only defined on Synapses, not
    # NeuronGroup. Omit that undefined assignment, keeping v/g reset unchanged.
    params['eq_rst'] = 'v = v_rst; g = 0 * mV'
    experiment = get_experiment('sugar')
    params['r_poi'] = experiment['stim_rate'] * b.Hz
    comp = repo / 'data' / '2025_Completeness_783.csv'
    conn = repo / 'data' / '2025_Connectivity_783.parquet'
    metadata = pd.read_csv(comp, index_col=0)
    indices = {int(root_id): index for index, root_id in enumerate(metadata.index)}
    activated = [indices[root_id] for root_id in experiment['neu_exc']]
    start = time.perf_counter()
    neurons, synapses, monitor = module.create_model(comp, conn, params)
    inputs, neurons = module.poi(neurons, activated, [], params)
    network = b.Network(neurons, synapses, monitor, *inputs)
    built = time.perf_counter()
    network.run(20 * b.ms)
    finished = time.perf_counter()
    assert np.isfinite(np.asarray(neurons.v / b.mV)).all()
    active = np.asarray(monitor.count) > 0
    return dict(revision=revision(repo), neurons=len(neurons), directed_connection_rows=len(synapses),
        stimulation='upstream sugar GRNs at 200 Hz; random seed 42',
        stimulated_neurons=len(activated), simulated_seconds=.02,
        setup_seconds=built-start, simulation_wall_seconds=finished-built,
        real_time_ratio=.02/(finished-built), spike_count=int(monitor.num_spikes),
        active_neurons=int(active.sum()), downstream_active_neurons=int(active.sum()-active[activated].sum()),
        finite_membrane_state=True, backend='Brian2 NumPy CPU',
        adaptation='removed undefined neuronal reset assignment w=0',
        interpretation='load/stimulation/propagation smoke test, not reproduction of biological accuracy')

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=['body', 'brain'])
    args = parser.parse_args()
    try:
        result = dict(status='passed', **(body_test() if args.mode == 'body' else brain_test()))
    except Exception as exc:
        result = dict(status='failed', error=str(exc), traceback=traceback.format_exc())
    path = OUT / (args.mode + '-evaluation.json')
    path.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2), flush=True)
    return 0 if result['status'] == 'passed' else 1

if __name__ == '__main__':
    raise SystemExit(main())
