"""Contracts for future coupling; rider requests do not masquerade as neural rates."""
from dataclasses import dataclass
import math

@dataclass(frozen=True)
class RiderCues:
    turn: float = 0.
    climb: float = 0.
    spur_press: bool = False
    brake_press: bool = False
    land_hold: bool = False

    def __post_init__(self):
        if any(type(value) is not bool for value in (self.spur_press,self.brake_press,self.land_hold)):
            raise ValueError('Rider cue events must be booleans')
        for value in (self.turn, self.climb):
            if not math.isfinite(value) or not -1 <= value <= 1:
                raise ValueError('Rein requests must be finite and within [-1, 1]')

@dataclass(frozen=True)
class NeuralPopulation:
    name: str
    root_ids: tuple[str, ...]
    annotation_source: str
    dataset_version: str = '783'

    def indices(self, root_to_index):
        if not self.annotation_source or self.dataset_version != '783' or not self.root_ids:
            raise ValueError('A version-matched annotated population is required')
        if len(set(self.root_ids)) != len(self.root_ids):
            raise ValueError('Duplicate neuron IDs would bias population rates')
        result = []
        for root_id in self.root_ids:
            if not isinstance(root_id, str) or not root_id.isdecimal():
                raise ValueError('Root IDs must be decimal strings, never JSON floating-point numbers')
            if int(root_id) not in root_to_index:
                raise ValueError('Annotated neuron is absent from this neural model: '+root_id)
            result.append(root_to_index[int(root_id)])
        return tuple(result)

def population_rate_hz(counts, window_seconds):
    """Mean spikes/s/neuron over a simulation-time window, not a wall-time window."""
    if not math.isfinite(window_seconds) or window_seconds <= 0 or not counts:
        raise ValueError('A positive simulation window and nonempty population are required')
    if any(not isinstance(count, int) or count < 0 for count in counts):
        raise ValueError('Spike counts must be nonnegative integers')
    return sum(counts)/(len(counts)*window_seconds)

def require_flight_calibration(populations, calibration):
    """No arbitrary rate-to-wing mapping is enabled by these definitions."""
    required = {'flight_power', 'flight_steering', 'flight_pitch'}
    if not required.issubset(populations) or not calibration or not calibration.get('validation_report'):
        raise RuntimeError('Neural flight decoding unavailable: verified populations and a tested calibration are required')
    return calibration
