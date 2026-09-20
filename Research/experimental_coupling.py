"""Declared engineering hypotheses, not a calibrated sensory/motor model."""
from dataclasses import dataclass
import math
from brain_body_interface import RiderCues

@dataclass(frozen=True)
class BodyFeedback:
    speed_mps: float
    height_m: float
    vertical_mps: float
    yaw_rate_rps: float
    def __post_init__(self):
        if not all(math.isfinite(x) for x in self.__dict__.values()):
            raise ValueError('Non-finite body feedback')

class ExperimentalEncoder:
    """Feedback controller driving artificial DNg02 excitation, not sensory neurons."""
    def __init__(self): self.cruise=.15; self.burst=0.; self.filtered=(120.,120.)
    def encode(self, cues, feedback, dt=.015):
        if dt <= 0 or not math.isfinite(dt): raise ValueError('Invalid simulation window')
        if cues.spur_press: self.cruise=min(.35,self.cruise+.02); self.burst=.06
        if cues.brake_press: self.cruise=max(.03,self.cruise-.02); self.burst=0
        self.burst *= math.exp(-dt/.15)
        target_speed = .03 if cues.land_hold else self.cruise+self.burst
        power = 120+120*(target_speed-feedback.speed_mps)
        power += 30*(.04*cues.climb-feedback.vertical_mps)
        turn = 20*(cues.turn-feedback.yaw_rate_rps*.1)
        alpha=1-math.exp(-dt/.03)
        requested=(power-turn,power+turn)
        self.filtered=tuple(old+alpha*(max(0,min(250,new))-old) for old,new in zip(self.filtered,requested))
        return dict(left=self.filtered[0],right=self.filtered[1])

def decode_rates(rates):
    """Small bounded wing residuals from actual counted spikes; arbitrary test gains."""
    left,right=float(rates['left']),float(rates['right'])
    if not all(math.isfinite(x) and x >= 0 for x in (left,right)): raise ValueError('Invalid population rate')
    amplitude=max(-.015,min(.015,((left+right)/2-100)*.0001))
    differential=max(-.01,min(.01,(right-left)*.0001))
    return dict(left=amplitude-differential,right=amplitude+differential)
