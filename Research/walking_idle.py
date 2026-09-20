"""Bounded engineering idle bouts on live MuJoCo legs; not biological decision-making."""
import numpy as np
class WalkingIdle:
    def __init__(self):self.home=None;self.next_walk=None;self.walk_until=None;self.state='rider control'
    def update(self,body,perch,enabled,surface):
        if body.rider_attached or not enabled or surface!='floor' or not perch:
            self.home=self.next_walk=self.walk_until=None;self.state='held' if not body.rider_attached else 'rider control'
            return None
        now=float(body.d.time)
        if self.home is None:
            self.home=body.d.qpos[:2].copy();self.next_walk=now+2.;self.state='resting'
        distance=float(np.linalg.norm(body.d.qpos[:2]-self.home))
        if self.walk_until is not None:
            if now>=self.walk_until or distance>=2.:
                perch.land();self.walk_until=None;self.next_walk=now+3.;self.state='resting'
                return (0.,0.,True)
            self.state='walking';return (.55,0.,False)
        if perch.phase=='perched' and now>=self.next_walk and distance<1.8:
            perch.resume_walk();self.walk_until=now+.15;self.state='walking';return (.55,0.,False)
        self.state='resting';return (0.,0.,True)
