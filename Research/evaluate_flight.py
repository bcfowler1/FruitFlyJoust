"""Evaluate released flight task mechanics without claiming a trained policy."""
import json
from pathlib import Path
import sys
import time
import numpy as np

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT/'work/research/flybody'))
from flybody.fly_envs import flight_imitation
from flybody.tasks.trajectory_loaders import constant_speed_trajectory

def trial(frequency_action):
    env = flight_imitation(random_state=np.random.RandomState(42), terminal_com_dist=float('inf'))
    qpos, qvel = constant_speed_trajectory(n_steps=3000, speed=20, init_pos=(0, 0, 1),
        body_rot_angle_y=-47.5, control_timestep=env.control_timestep())
    env.task._traj_generator.set_next_trajectory(qpos, qvel)
    start = time.perf_counter()
    timestep = env.reset()
    spec = env.action_spec()
    action = np.zeros(spec.shape, dtype=spec.dtype)
    user = env.task._walker._action_indices['user'][0]
    action[user] = frequency_action
    wing_joints = env.task._wing_joints
    first = env.physics.bind(wing_joints).qpos.copy()
    max_wing_change = 0.
    steps = 0
    initial_com = env.physics.named.data.subtree_com['walker/'].copy()
    for _ in range(1000):
        # Upstream before_step mutates action by adding the baseline wing pattern.
        timestep = env.step(action.copy())
        steps += 1
        max_wing_change = max(max_wing_change, float(np.max(np.abs(env.physics.bind(wing_joints).qpos-first))))
        if timestep.last(): break
    elapsed = time.perf_counter()-start
    result = dict(frequency_action=frequency_action, frequency_hz=218*(1+.05*frequency_action),
        control_steps=steps, simulation_seconds=float(env.physics.data.time), wall_seconds=elapsed,
        action_shape=list(spec.shape), physics_timestep=env.physics.timestep(),
        control_timestep=env.control_timestep(), max_wing_angle_change_rad=max_wing_change,
        initial_com_m=(initial_com*.01).tolist(),
        final_com_m=(env.physics.named.data.subtree_com['walker/']*.01).tolist(),
        finite=bool(np.isfinite(env.physics.data.qpos).all() and np.isfinite(env.physics.data.qvel).all()),
        episode_terminated=bool(timestep.last()),
        reached_reference_end=bool(env.task._reached_traj_end),
        final_thorax_height_cm=float(env.task._walker.observables.thorax_height(env.physics)),
        final_acceleration_norm=float(np.linalg.norm(env.physics.data.qacc)),
        observation_names=sorted(timestep.observation.keys()))
    assert result['finite'] and max_wing_change>0
    env.close()
    return result

if __name__=='__main__':
    results = [trial(a) for a in [-1., 0., 1.]]
    report=dict(status='mechanics checks passed', trials=results, trained_policy=False,
        measured_wing_pattern=False, neural_coupling=False,
        interpretation='Upstream approximate wingbeat plus zero policy residual; not controlled flight or biological validation')
    Path(__file__).with_name('flight-evaluation.json').write_text(json.dumps(report, indent=2))
    print(json.dumps(report, indent=2))
