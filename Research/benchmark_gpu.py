import json
from pathlib import Path
import time
import traceback
from gpu_backend import NeuralRuntime

OUT = Path(__file__).resolve().parent

def parity_test(source, torch):
    # Identical deterministic stimulation on CPU/GPU; no cross-device RNG assumption.
    class Stimulus(torch.nn.Module):
        def forward(self, rates, generator=None): return rates
    indices = torch.tensor([[1, 2, 3, 4, 5, 6, 7, 0], [0, 1, 2, 3, 4, 5, 6, 7]])
    base = torch.sparse_coo_tensor(indices, torch.ones(8) * 8, (8, 8)).coalesce().to_sparse_csr()
    states, models, rates = [], [], []
    for device in ['cpu', 'cuda']:
        model = source.TorchModel(1, 8, source.DT, source.MODEL_PARAMS, base.to(device), [0], device)
        model.poisson = Stimulus()
        models.append(model)
        states.append(model.state_init())
        rate = torch.zeros(1, 8, device=device); rate[0, 0] = 30
        rates.append(rate)
    with torch.no_grad():
        for _ in range(300):
            for index in range(2): states[index] = models[index](rates[index], *states[index])
            assert torch.equal(states[0][2], states[1][2].cpu()), 'CPU/GPU spike mismatch'
            assert torch.allclose(states[0][3], states[1][3].cpu(), atol=2e-4, rtol=1e-5), 'CPU/GPU voltage mismatch'
    return '300 deterministic steps: matching spikes and membrane voltages within tolerance'

def main():
    try:
        start = time.perf_counter()
        runtime = NeuralRuntime()
        runtime.synchronize()
        setup = time.perf_counter() - start
        torch = runtime.torch
        parity = parity_test(runtime.source, torch)
        runtime.step(200); runtime.synchronize() # warm-up, excluded from measured windows
        windows = []
        for steps in [150, 500, 1000]:
            start = time.perf_counter()
            runtime.step(steps); runtime.synchronize()
            elapsed = time.perf_counter()-start
            simulated = steps * runtime.source.DT / 1000
            windows.append(dict(steps=steps, simulated_seconds=simulated,
                wall_seconds=elapsed, real_time_ratio=simulated/elapsed))
        graph_results = []
        try:
            runtime.enable_graph(150)
            before = runtime.summary()
            for repeats in [1, 4, 10]:
                start = time.perf_counter()
                runtime.step(150 * repeats); runtime.synchronize()
                elapsed = time.perf_counter() - start
                graph_results.append(dict(simulated_seconds=.015*repeats, wall_seconds=elapsed,
                    real_time_ratio=.015*repeats/elapsed))
            after = runtime.summary()
            assert after['finite_membrane_state'] and after['spike_count'] > before['spike_count']
            graph_status = 'passed; persistent state advances and spike counts increase across replay'
        except Exception as exc:
            graph_status = str(exc)
        result = dict(status='passed', torch_version=torch.__version__, cuda_version=torch.version.cuda,
            device=torch.cuda.get_device_name(0), capability=torch.cuda.get_device_capability(0),
            setup_seconds=setup, cpu_gpu_parity=parity, measured_windows=windows,
            graph_status=graph_status, graph_windows=graph_results,
            allocated_gpu_bytes=torch.cuda.memory_allocated(), **runtime.summary(),
            interpretation='upstream PyTorch neural model benchmark; no body/flight controller coupling')
    except Exception as exc:
        result = dict(status='failed', error=str(exc), traceback=traceback.format_exc())
    (OUT / 'gpu-evaluation.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2), flush=True)
    return 0 if result['status'] == 'passed' else 1

if __name__ == '__main__': raise SystemExit(main())
