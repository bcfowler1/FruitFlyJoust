"""Declared engineering neutral-neck controller plus unchanged trained wings."""
import numpy as np

def motor_action(policy_action,task,residual,influence):
    action=np.asarray(policy_action).copy()
    # Native position actuators provide PD control to a neutral neck target.
    # The published unconstrained Gaussian head mean can exceed its action
    # bounds even without neural wing input; it is not used in this controller.
    action[task._walker._action_indices['head']]=0.
    wings=task._wing_inds_action
    for side,indices in zip(('left','right'),(wings[:3],wings[3:])):
        action[indices[0]]+=residual[side]*influence
    return action
