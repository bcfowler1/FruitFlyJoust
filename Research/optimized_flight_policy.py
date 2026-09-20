"""Tensor-returning graph wrapper around the unchanged published policy."""
from evaluate_trained_flight import tf

class CompiledMeanPolicy:
    def __init__(self, restored):
        self.restored=restored
        self.mean=tf.function(lambda observation: restored(observation).mean(),autograph=False)
    def __call__(self, observation):
        # Match the distribution-mean interface without returning a TFP composite.
        tensor=self.mean(observation)
        class Mean:
            def mean(self): return tensor
        return Mean()
