"""Cache fixed actuator topology; preserve upstream action application semantics."""
from types import MethodType
import numpy as np

def cache_actuator_mapping(env):
    walker=env.task._walker
    # The MJCF topology is fixed after environment construction; reset must not rebuild legs.
    has_actuators=bool(walker.mjcf_model.find_all('actuator'))
    mappings=[(np.asarray(walker._ctrl_indices[key],dtype=int),np.asarray(indices,dtype=int))
        for key,indices in walker._action_indices.items() if walker._ctrl_indices[key] and indices]
    def apply_action(self,physics,action,random_state):
        if not has_actuators:return
        self._prev_action[:]=action
        ctrl=np.zeros(physics.model.nu)
        for control,selection in mappings:ctrl[control]=action[selection]
        physics.set_control(ctrl)
    walker.apply_action=MethodType(apply_action,walker)
    return env
