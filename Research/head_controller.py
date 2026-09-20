"""FlyGym 1.1 upstream trained head-stabilization MLP, NumPy CPU inference.

Architecture, normalization and contact mask follow FlyGym's Apache-2.0
HeadStabilizationInferenceWrapper. Weights are unchanged; see parity artifact.
This is a trained proprioceptive controller, not FlyWire head-neuron decoding.
"""
from pathlib import Path
import numpy as np
class HeadController:
    def __init__(self):
        with np.load(Path(__file__).with_name('head-controller.npz')) as source:self.weights={k:source[k] for k in source.files}
        self.enabled=True
    def __call__(self,joint_angles,contact_forces):
        if not self.enabled:return np.zeros(2)
        w=self.weights
        angles=(joint_angles-w['mean'])/w['std']
        contacts=np.linalg.norm(contact_forces,axis=1).reshape(6,6).sum(axis=1)>=np.array([.5,1,3,.5,1,3])
        x=np.concatenate([angles,contacts],dtype=np.float32)
        for i in (1,2,3):
            x=w[f'layer{i}_weight']@x+w[f'layer{i}_bias']
            if i<3:x=np.maximum(x,0)
        return x
