"""Reuse an observation only within an unchanged FlyGym physics time.

The hybrid controller reads the preceding post-step observation again before
applying its next action. Clear this cache on reset or an external state edit.
"""
from types import MethodType

def install(body):
    original=body.fly.get_observation
    body.observation_cache={}
    def observation(fly,sim):
        stamp=float(sim.physics.data.time)
        cache=body.observation_cache
        if cache.get('time')!=stamp:
            cache['value']=original(sim);cache['time']=stamp
        return cache['value']
    body.fly.get_observation=MethodType(observation,body.fly)
