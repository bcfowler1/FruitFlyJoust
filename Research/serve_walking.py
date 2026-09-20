"""Unity UDP stream for a separate NeuroMechFly walking lab."""
import argparse,json,socket,time,uuid
import numpy as np
from walking_backend import WalkingBody

def main():
    p=argparse.ArgumentParser();p.add_argument('--experimental-neural',action='store_true');p.add_argument('--export',action='store_true')
    p.add_argument('--duration',type=float,default=0)
    p.add_argument('--calibrated-steering',action='store_true')
    p.add_argument('--perch-transitions',action='store_true')
    p.add_argument('--optimized',action='store_true')
    p.add_argument('--head-stabilization',action='store_true')
    p.add_argument('--upstream-steering',action='store_true')
    p.add_argument('--surface',choices=('floor','wall','ceiling'),default='floor');args=p.parse_args()
    if args.upstream_steering:
        from pathlib import Path
        if not args.experimental_neural or not args.calibrated_steering or not args.head_stabilization or args.surface!='floor':
            raise RuntimeError('Upstream steering requires full brain, calibrated floor steering and trained head')
        for report_name in ('neural-upstream-dependency-evaluation.json','upstream-gain-0.32-middle-sensory-tau-0.12-evaluation.json','upstream-gain-0.32-range-sensory-tau-0.12-evaluation.json'):
            report=json.loads(Path(__file__).with_name(report_name).read_text())
            if not report.get('qualified',report.get('network_changes_current_steering_readout',False)):
                raise RuntimeError('Upstream steering qualification failed: '+report_name)
            if 'trials' in report and (report.get('sensory_yaw_tau')!=.12 or any(t.get('yaw_feedback_gain')!=.32 for t in report['trials'])):
                raise RuntimeError('Upstream settings do not match qualification: '+report_name)
    if args.perch_transitions:
        from pathlib import Path
        trials=json.loads(Path(__file__).with_name('walking-perch-evaluation.json').read_text())['trials']
        if not any(t['surface']==args.surface and t['passed'] for t in trials):
            raise RuntimeError('Contact transition qualification failed for '+args.surface)
    if args.optimized:
        from pathlib import Path
        if not json.loads(Path(__file__).with_name('walking-performance-evaluation.json').read_text())['qualified']:
            raise RuntimeError('Walking observation cache parity failed')
    if args.head_stabilization and args.surface!='floor':
        raise RuntimeError('Trained head stabilization is currently qualified only for floor walking')
    if args.head_stabilization:
        from pathlib import Path
        if not json.loads(Path(__file__).with_name('head-walking-evaluation.json').read_text())['qualified']:
            raise RuntimeError('Loaded head-control walking qualification failed')
    body=WalkingBody(optimized=args.optimized,head_stabilization=args.head_stabilization);brain=None;controller=None;perch=None
    from walking_idle import WalkingIdle
    idle=WalkingIdle();autonomous_idle=False
    try:
        if args.export:print(json.dumps(body.export()));return
        if args.calibrated_steering:
            from pathlib import Path
            qualification=json.loads(Path(__file__).with_name('head-walking-controller-evaluation.json' if args.head_stabilization else 'walking-controller-evaluation.json').read_text())
            if not qualification['qualified']:raise RuntimeError('Walking controller qualification failed')
            from walking_steering import WalkingSteering
            controller=WalkingSteering(.32 if args.upstream_steering else .12)
        sensory_yaw=0.
        def reset_body():
            nonlocal perch,sensory_yaw
            sensory_yaw=0.
            body.set_rider_attached(True)
            body.m.opt.gravity[:]=(0,0,-9810);body.reset()
            if controller:controller.reset()
            if args.perch_transitions:
                from walking_perch import WalkingPerch
                perch=WalkingPerch(body)
                if args.surface!='floor':
                    for _ in range(30):perch.step([.7,.7])
                    perch.land()
                    for _ in range(100):
                        perch.step([0,0])
                        if perch.phase=='perched':break
                    if perch.phase!='perched':raise RuntimeError('Initial contact handover failed')
                    body.m.opt.gravity[:]=dict(wall=(-9810,0,0),ceiling=(0,0,9810))[args.surface]
                    for _ in range(30):perch.step([0,0])
                    if len(body.contacts())<3:raise RuntimeError('Initial surface grip failed')
                    body.d.time=0.;body.sim.curr_time=0.
        reset_body()
        if args.experimental_neural:
            from neural_flight_client import NeuralFlightClient
            brain=NeuralFlightClient('neural_walking_worker.py','neural-walking-worker.log',('--diagnostic-pfl3',) if args.upstream_steering else ())
        session=uuid.uuid4().hex;seq=0;paused=False;turn=climb=0.;influence=1.;cruise=.7;burst=0.;hold=False
        grip=1.;steering_state={}
        spur=brake=False;neural_state=None;start=time.perf_counter()
        with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as sock:
            sock.bind(('127.0.0.1',55370));sock.setblocking(False)
            print('WALKING_READY: NeuroMechFly v2 body; rigid 1/3 mass rider; '+('experimental full connectome' if brain else 'brain off'),flush=True)
            while not args.duration or time.perf_counter()-start<args.duration:
                while True:
                    try:raw,peer=sock.recvfrom(4096)
                    except (BlockingIOError,ConnectionResetError):break
                    if peer[0]!='127.0.0.1':continue
                    try:
                        r=json.loads(raw);t=float(r.get('turn',0));i=float(r.get('neural_influence',1))
                        vertical=float(r.get('climb',0));g=float(r.get('adhesion_strength',1))
                        if r.get('protocol')!=1 or not np.isfinite([t,i,vertical,g]).all() or not -1<=t<=1 or not -1<=vertical<=1 or not 0<=i<=1 or not 0<=g<=1:continue
                        turn=t;climb=vertical;grip=g;influence=i;hold=r.get('land_hold') is True
                        autonomous_idle=r.get('autonomous_idle') is True
                        attached=r.get('rider_attached',body.rider_attached)
                        if type(attached) is bool and perch and perch.phase=='perched' and len(body.contacts())>=3:
                            body.set_rider_attached(attached)
                        spur|=r.get('spur_press') is True;brake|=r.get('brake_press') is True
                        if type(r.get('paused')) is bool:paused=r['paused']
                        if r.get('reset') is True:
                            reset_body()
                            if brain:brain.reset()
                            neural_state=None;steering_state={};session=uuid.uuid4().hex;seq=0;cruise=.7;burst=0;spur=brake=False
                            idle=WalkingIdle()
                    except (ValueError,TypeError,AttributeError):continue
                tick=time.perf_counter()
                if not paused and not body.ended:
                    idle_action=idle.update(body,perch,autonomous_idle,args.surface)
                    effective_hold=hold if idle_action is None else idle_action[2]
                    if perch:
                        perch.strength=grip
                        if effective_hold:perch.land()
                        elif perch.phase=='perched' and spur:
                            perch.launch();spur=False
                        elif perch.phase=='perched' and abs(climb)>.2:
                            perch.resume_walk()
                        if controller and perch.phase!='walking':controller.reset()
                    if brake:cruise=max(0,cruise-.15);burst=0
                    elif spur:cruise=min(1,cruise+.1);burst=.3
                    spur=brake=False;speed=0 if effective_hold else min(1.3,cruise+burst);burst*=np.exp(-.015/.15)
                    steering=turn
                    if idle_action is not None:speed,steering,_=idle_action
                    feedback=body.feedback()
                    if brain:
                        sensory_feedback=dict(feedback)
                        if args.upstream_steering:
                            sensory_yaw+=(1-np.exp(-.015/.12))*(feedback['yaw_rate_radps']-sensory_yaw)
                            sensory_feedback['yaw_rate_radps']=float(sensory_yaw)
                        neural_state=brain.send(dict(turn=steering,**sensory_feedback))
                        rates=neural_state['rates']
                        steering=controller.neural_cue(rates,steering,influence) if controller else (1-influence)*steering+influence*np.clip((rates['right']-rates['left'])/100,-1,1)
                    if controller and args.surface=='floor':action,steering_state=controller.update(speed,steering,feedback['yaw_rate_radps'])
                    else:
                        action=np.clip([speed+.4*steering*speed,speed-.4*steering*speed],0,1.5)
                        steering_state=dict(qualified_range=False,mode='raw engineering drive')
                    if perch:perch.step(action)
                    else:body.step(action)
                elapsed=time.perf_counter()-tick
                frame=body.packet(session,seq,paused,elapsed,neural_state)
                frame.update(steering_calibration=steering_state,body_feedback=body.feedback(),contacting_claws=len(body.contacts()),
                    perch_phase=perch.phase if perch else '',adhesion_enabled=perch is not None and perch.phase in ('settling','approaching','verifying','perched') and grip>0,
                    walking_surface=args.surface,grip_strength=grip)
                frame['assistance_active']=bool(np.any(body.d.qfrc_applied) or np.any(body.d.xfrc_applied))
                frame['rider_attached']=body.rider_attached
                frame['optimized_observations']=args.optimized
                frame['idle_behavior']=idle.state
                frame['head_stabilization']=args.head_stabilization
                frame['upstream_steering']=args.upstream_steering
                frame['sensory_yaw_tau']=.12 if args.upstream_steering else 0
                frame['yaw_feedback_gain']=controller.yaw_feedback_gain if controller else 0
                sock.sendto(json.dumps(frame,separators=(',',':')).encode(),('127.0.0.1',55371));seq+=1
                time.sleep(max(0,.015-elapsed))
    finally:
        if brain:brain.close()
        body.close()

if __name__=='__main__':main()
