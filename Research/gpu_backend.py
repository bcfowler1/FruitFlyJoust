"""Persistent wrapper around the pinned upstream connectome model."""
from pathlib import Path
import sys
import time

ROOT = Path(__file__).resolve().parents[3]
REPO = ROOT / 'work' / 'research' / 'fly-brain'

class NeuralRuntime:
    def __init__(self, device='cuda', experiment='sugar', populations=None,connectivity_scale=1):
        # Upstream requires this import order on Windows.
        import pyarrow
        import torch
        sys.path.insert(0, str(REPO / 'code'))
        import run_pytorch as source
        from benchmark import get_experiment
        self.torch, self.source = torch, source
        if device == 'cuda' and not torch.cuda.is_available():
            raise RuntimeError('CUDA is unavailable; no silent CPU fallback in GPU mode')
        self.device = device
        settings = get_experiment(experiment)
        mapping, _ = source.get_hash_tables(REPO / 'data' / '2025_Completeness_783.csv')
        self.root_to_index = mapping
        self.exc = [mapping[root_id] for root_id in settings['neu_exc']]
        cache = ROOT / 'work' / 'neural-weights'
        cache.mkdir(exist_ok=True)
        weights = source.get_weights(REPO / 'data' / '2025_Connectivity_783.parquet',
            REPO / 'data' / '2025_Completeness_783.csv', cache, csr=True).to(device)
        if connectivity_scale not in (0,1):raise ValueError('Connectivity scale must be 1, or 0 for diagnostic isolation')
        if connectivity_scale==0:weights.values().zero_() # Device copy only; source/cache untouched.
        self.size = weights.shape[0]
        self.connection_rows = weights._nnz()
        self.model = source.TorchModel(1, self.size, source.DT, source.MODEL_PARAMS,
            weights, exc_indices=self.exc, device=device).eval()
        self.state = self.model.state_init()
        self.rates = torch.zeros((1, self.size), device=device)
        self.rates[:, self.exc] = settings['stim_rate']
        self.generator = torch.Generator(device=device).manual_seed(42)
        self.simulated_ms = 0.
        self.step_count = 0
        self.active = torch.zeros(self.size, dtype=torch.bool, device=device)
        self.spike_total = torch.zeros((), device=device)
        self.graph = None
        self.graph_steps = 0
        self.monitored = {}
        self.population_counts = {}
        for name, population in (populations or {}).items():
            indices = population.indices(mapping)
            self.monitored[name] = torch.tensor(indices, dtype=torch.long, device=device)
            self.population_counts[name] = torch.zeros(len(indices), device=device)

    def enable_graph(self, steps=150):
        torch = self.torch
        if self.device != 'cuda': raise RuntimeError('CUDA graphs require a GPU')
        stream = torch.cuda.Stream()
        stream.wait_stream(torch.cuda.current_stream())
        with torch.cuda.stream(stream):
            for _ in range(3): self.step(steps)
        torch.cuda.current_stream().wait_stream(stream)
        self.synchronize()
        # Keep stable source state addresses; each replay writes its final state
        # back to these buffers, so the next replay continues rather than restarts.
        self.state = tuple(t.clone() for t in self.state)
        graph = torch.cuda.CUDAGraph()
        graph.register_generator_state(self.generator)
        with torch.no_grad(), torch.cuda.graph(graph):
            state = self.state
            for _ in range(steps):
                state = self.model(self.rates, *state, generator=self.generator)
                self.active |= state[2][0] > 0
                self.spike_total += state[2].sum()
                for name, indices in self.monitored.items():
                    self.population_counts[name] += state[2][0, indices]
            for persistent, final in zip(self.state, state): persistent.copy_(final)
        self.graph, self.graph_steps = graph, steps

    def step(self, steps, rate_hz=None):
        torch = self.torch
        if rate_hz is not None:
            self.rates[:, self.exc] = max(0., min(1000., rate_hz))
        if self.graph is not None:
            if steps % self.graph_steps: raise ValueError('Graph stepping must use whole captured chunks')
            for _ in range(steps // self.graph_steps): self.graph.replay()
            self.step_count += steps
            self.simulated_ms += steps * self.source.DT
            return
        with torch.no_grad():
            for _ in range(steps):
                self.state = self.model(self.rates, *self.state, generator=self.generator)
                spikes = self.state[2]
                self.active |= spikes[0] > 0
                self.spike_total += spikes.sum()
                for name, indices in self.monitored.items():
                    self.population_counts[name] += spikes[0, indices]
        self.step_count += steps
        self.simulated_ms += steps * self.source.DT

    def synchronize(self):
        if self.device == 'cuda': self.torch.cuda.synchronize()

    def reset(self):
        """Reset state in place so captured graph pointers remain valid."""
        self.synchronize()
        initial=self.model.state_init()
        for persistent,value in zip(self.state,initial): persistent.copy_(value)
        self.rates.zero_(); self.active.zero_(); self.spike_total.zero_()
        for counts in self.population_counts.values(): counts.zero_()
        self.generator.manual_seed(42)
        self.simulated_ms=0.; self.step_count=0

    def summary(self):
        self.synchronize()
        return dict(neurons=self.size, directed_connection_rows=self.connection_rows,
            simulated_ms=self.simulated_ms, spike_count=int(self.spike_total.item()),
            active_neurons=int(self.active.sum().item()),
            downstream_active_neurons=int(self.active.sum().item()-self.active[self.exc].sum().item()),
            finite_membrane_state=bool(self.torch.isfinite(self.state[3]).all().item()))

    def population_summary(self):
        self.synchronize()
        return {name: dict(spike_counts=[int(value) for value in counts.cpu().tolist()],
            window_seconds=self.simulated_ms/1000) for name, counts in self.population_counts.items()}
