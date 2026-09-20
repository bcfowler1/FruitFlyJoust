"""Loaded trained flight with an optional explicitly experimental connectome loop."""
import argparse
import json
import socket
import time
import uuid
import mujoco
from evaluate_trained_flight import ASSETS, ROOT, tf, np, flight_imitation
from export_body import visible_geoms, export_geometry
from optimized_flight_policy import CompiledMeanPolicy
from brain_body_interface import RiderCues
from rider_load import RiderLoad
from optimized_environment import cache_actuator_mapping
from flight_motor_controller import motor_action

def create_environment(index):
    return flight_imitation(ref_path=str(ASSETS/'flight-dataset_saccade-evasion_augmented.hdf5'),
        wpg_pattern_path=str(ASSETS/'wing_pattern_fmech.npy'), randomize_start_step=False,
        traj_indices=[index], random_state=np.random.RandomState(42))

def flight_geoms(model):
    # Exclude the reference ghost: only the physical walker is streamed.
    return [i for i in visible_geoms(model)
        if (mujoco.mj_id2name(model, mujoco.mjtObj.mjOBJ_GEOM, i) or '').startswith('walker/')]

def state_packet(env, geoms, anchor, session, seq, paused, ended, elapsed, advanced, clipped):
    data=env.physics.data.ptr
    rotations=[]
    for i in geoms:
        quat=np.empty(4)
        mujoco.mju_mat2Quat(quat,data.geom_xmat[i])
        rotations.extend(quat.tolist())
    return dict(protocol=1,session=session,seq=seq,sim_time=float(data.time),
        neural_time=0,neurons=0,connections=0,spikes=0,active=0,rate_hz=0,
        compute_ratio=advanced/max(elapsed,.000001),paused=paused or ended,episode_ended=ended,
        episode_status=('reference completed' if env.task._reached_traj_end else 'task terminated') if ended else 'running',
        clipped_actions=clipped,
        positions=(data.geom_xpos[geoms]*.01).ravel().tolist(),rotations=rotations,
        anchor_position=(data.xpos[anchor]*.01).tolist(),anchor_rotation=data.xquat[anchor].tolist(),
        body_controller='published trained flight policy; connectome coupling not implemented')

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--duration',type=float,default=0)
    parser.add_argument('--trajectory',type=int,default=1)
    parser.add_argument('--input-port',type=int,default=55370)
    parser.add_argument('--output-port',type=int,default=55371)
    parser.add_argument('--export',action='store_true')
    parser.add_argument('--rider-mass-fraction',type=float,default=1/3)
    parser.add_argument('--experimental-neural',action='store_true',help='Unvalidated synthetic descending excitation and small wing residuals')
    args=parser.parse_args()
    env=cache_actuator_mapping(create_environment(args.trajectory))
    neural=None
    try:
        timestep=env.reset()
        load=RiderLoad(env.physics,args.rider_mass_fraction)
        load_report=load.apply(env.physics)
        model=env.physics.model.ptr
        geoms=flight_geoms(model)
        if not geoms: raise RuntimeError('Physical walker visual geometry not found')
        if args.export:
            export_geometry(model,geoms,ROOT/'outputs/FruitFlyJoust/Assets/Resources/FlyFlightGeometry.bytes')
            return
        anchor=mujoco.mj_name2id(model,mujoco.mjtObj.mjOBJ_BODY,'walker/thorax')
        if anchor<0: raise RuntimeError('Physical thorax anchor not found')
        policy=CompiledMeanPolicy(tf.saved_model.load(str(ASSETS/'flight')))
        if args.experimental_neural:
            from neural_flight_client import NeuralFlightClient
            neural=NeuralFlightClient()
        neural_state=None; residual=dict(left=0.,right=0.); cues=RiderCues()
        spec=env.action_spec()
        session=uuid.uuid4().hex
        seq=0; paused=False; ended=False; clipped=0; cue_events=0
        neural_influence=1.
        with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as sock:
            sock.bind(('127.0.0.1',args.input_port)); sock.setblocking(False)
            start=time.perf_counter()
            print('FLIGHT_READY: ' + ('EXPERIMENTAL connectome residuals + trained stabilizer' if neural else
                'published trained policy, measured wing pattern; no neural motor coupling'),flush=True)
            while not args.duration or time.perf_counter()-start<args.duration:
                while True:
                    try: raw,peer=sock.recvfrom(4096)
                    except BlockingIOError: break
                    except ConnectionResetError: continue
                    if peer[0]!='127.0.0.1': continue
                    try:
                        request=json.loads(raw)
                        if request.get('protocol')!=1: continue
                        influence=float(request.get('neural_influence',neural_influence))
                        if not np.isfinite(influence) or not 0 <= influence <= 1: continue
                        neural_influence=influence
                        received=RiderCues(turn=float(request.get('turn',0)),climb=float(request.get('climb',0)),
                            spur_press=request.get('spur_press',False),brake_press=request.get('brake_press',False),
                            land_hold=request.get('land_hold',False))
                        cue_events+=int(received.spur_press)+int(received.brake_press)
                        # Preserve button edges across queued packets until the neural step consumes them.
                        cues=RiderCues(turn=received.turn,climb=received.climb,
                            spur_press=cues.spur_press or received.spur_press,
                            brake_press=cues.brake_press or received.brake_press,land_hold=received.land_hold)
                        # Synthetic feedback encoding is enabled only in the experimental mode.
                        if isinstance(request.get('paused'),bool): paused=request['paused']
                        if request.get('reset') is True:
                            timestep=env.reset(); load.apply(env.physics); ended=False
                            if neural: neural.reset()
                            neural_state=None; residual=dict(left=0.,right=0.)
                            cues=RiderCues(turn=received.turn,climb=received.climb,land_hold=received.land_hold)
                            session=uuid.uuid4().hex; seq=0; clipped=0
                    except (ValueError,TypeError,AttributeError): continue
                before=float(env.physics.data.time); tick=time.perf_counter()
                if not paused and not ended:
                    for _ in range(75): # 15 ms of simulated time per visual frame.
                        observation={key:tf.convert_to_tensor(value[None],dtype=tf.float32)
                            for key,value in timestep.observation.items()}
                        action=np.asarray(policy(observation).mean())[0]
                        if neural:
                            action=motor_action(action,env.task,residual,neural_influence)
                        if action.shape!=spec.shape or not np.isfinite(action).all():
                            raise RuntimeError('Invalid trained controller action')
                        clipped+=int(np.sum((action<spec.minimum)|(action>spec.maximum)))
                        timestep=env.step(np.clip(action,spec.minimum,spec.maximum).copy())
                        if not np.isfinite(env.physics.data.qpos).all() or not np.isfinite(env.physics.data.qvel).all():
                            raise RuntimeError('Non-finite flight state')
                        if timestep.last(): ended=True; break
                elapsed=time.perf_counter()-tick
                advanced=float(env.physics.data.time)-before
                if neural and advanced > 0:
                    neural_state=neural.step(env.physics,cues,advanced)
                    neural_state['consumed_rider_cues']=dict(cues.__dict__)
                    residual=neural_state['residual']
                    cues=RiderCues(turn=cues.turn,climb=cues.climb,land_hold=cues.land_hold)
                elapsed=time.perf_counter()-tick
                frame=state_packet(env,geoms,anchor,session,seq,paused,ended,elapsed,advanced,clipped)
                frame['rider_cue_events']=cue_events
                frame['rider_load']=load_report
                frame['neural_influence']=neural_influence
                frame['rider_cue_mapping']='disabled: neural sensory/flight calibration required'
                if neural_state:
                    neural_state['applied_residual']=dict((side,value*neural_influence) for side,value in residual.items())
                    stats=neural_state['stats']
                    frame.update(neural_time=neural_state['neural_time'],neurons=stats['neurons'],
                        connections=stats['directed_connection_rows'],spikes=stats['spike_count'],active=stats['active_neurons'],
                        body_controller='EXPERIMENTAL full connectome wing residuals + trained wing stabilizer; neutral native neck target',
                        rider_cue_mapping=neural_state['encoder'],experimental_neural=neural_state)
                payload=json.dumps(frame,separators=(',',':')).encode()
                if len(payload)>65000: raise RuntimeError('Flight frame exceeds UDP size')
                sock.sendto(payload,('127.0.0.1',args.output_port)); seq+=1
                if paused or ended: time.sleep(.03)
            print(json.dumps(dict(frames=seq,simulated_seconds=float(env.physics.data.time),
                clipped_action_entries=clipped,episode_ended=ended)),flush=True)
    finally:
        if neural: neural.close()
        env.close()

if __name__=='__main__': main()
