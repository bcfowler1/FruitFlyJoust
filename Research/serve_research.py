"""Loopback research stream: synchronized brain/body clocks, no invented motor decoding."""
import argparse
import json
import math
from pathlib import Path
import socket
import time
import uuid
import numpy as np
import mujoco
from export_body import body_model, visible_geoms
from gpu_backend import NeuralRuntime

def packet(model, data, geom_ids, brain, session, seq, rate, elapsed, brain_origin, paused):
    rotations = []
    for index in geom_ids:
        quat = np.empty(4)
        mujoco.mju_mat2Quat(quat, data.geom_xmat[index])
        rotations.extend(quat.tolist())
    stats = brain.summary()
    return dict(protocol=1, session=session, seq=seq, sim_time=float(data.time),
        neural_time=(brain.simulated_ms-brain_origin)/1000, neurons=stats['neurons'],
        connections=stats['directed_connection_rows'], spikes=stats['spike_count'],
        active=stats['active_neurons'], rate_hz=rate, compute_ratio=0 if paused else .015/elapsed,
        paused=paused,
        anchor_position=(data.xpos[mujoco.mj_name2id(model, mujoco.mjtObj.mjOBJ_BODY, 'thorax')]*.01).tolist(),
        anchor_rotation=data.xquat[mujoco.mj_name2id(model, mujoco.mjtObj.mjOBJ_BODY, 'thorax')].tolist(),
        positions=(data.geom_xpos[geom_ids] * .01).ravel().tolist(), rotations=rotations,
        body_controller='passive physics; neural-to-flight mapping not implemented')

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--duration', type=float, default=0, help='0: until stopped; otherwise bounded seconds')
    parser.add_argument('--input-port', type=int, default=55370)
    parser.add_argument('--output-port', type=int, default=55371)
    args = parser.parse_args()
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
        sock.bind(('127.0.0.1', args.input_port))
        sock.setblocking(False)
        model, data = body_model()
        geoms = visible_geoms(model)
        brain = NeuralRuntime()
        brain.enable_graph(150)
        brain_origin = brain.simulated_ms
        session = uuid.uuid4().hex
        rate, seq, paused = 0., 0, False
        start = time.perf_counter()
        print('RESEARCH_READY: full GPU brain + passive articulated body; UDP loopback', flush=True)
        while not args.duration or time.perf_counter() - start < args.duration:
            while True:
                try: raw, peer = sock.recvfrom(4096)
                except BlockingIOError: break
                # Windows reports a previous UDP send to an absent viewer here.
                except ConnectionResetError: continue
                if peer[0] != '127.0.0.1': continue
                try:
                    request = json.loads(raw)
                    if request.get('protocol') != 1: continue
                    value = float(request.get('rate_hz', rate))
                    if math.isfinite(value): rate = max(0., min(1000., value))
                    paused = bool(request.get('paused', paused))
                except (ValueError, TypeError, AttributeError): continue
            step_start = time.perf_counter()
            if not paused:
                brain.step(150, rate_hz=rate)
                brain.synchronize()
                for _ in range(150): mujoco.mj_step(model, data)
            elapsed = max(.000001, time.perf_counter()-step_start)
            state = packet(model, data, geoms, brain, session, seq, rate, elapsed, brain_origin, paused)
            assert abs(state['sim_time']-state['neural_time']) < 1e-7
            payload = json.dumps(state, separators=(',', ':')).encode('utf-8')
            if len(payload) > 65000: raise RuntimeError('State exceeds UDP packet limit')
            sock.sendto(payload, ('127.0.0.1', args.output_port))
            seq += 1
            if paused: time.sleep(.03)
        print(json.dumps(dict(frames=seq, simulated_seconds=float(data.time),
            neural_seconds=(brain.simulated_ms-brain_origin)/1000)), flush=True)

if __name__ == '__main__': main()
