"""Live transport qualification: full brain continues during unloaded idle gait."""
import json,socket,subprocess,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]
def main(head=False,upstream=False):
    with (ROOT/'work/walking-idle-stream.log').open('w') as log, socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as sock:
        sock.bind(('127.0.0.1',55371));sock.settimeout(60)
        process=subprocess.Popen([str(ROOT/'work/neuromechfly-env/Scripts/python.exe'),str(Path(__file__).with_name('serve_walking.py')),
            '--experimental-neural','--perch-transitions','--optimized']+(['--head-stabilization'] if head else ['--calibrated-steering'])+(['--upstream-steering','--calibrated-steering'] if upstream else []),stdout=log,stderr=log)
        def frame():
            f=json.loads(sock.recv(65000));assert f['neurons']==138639
            assert abs(f['sim_time']-f['neural_time'])<1e-8
            assert not f['episode_ended'];return f
        def request(**values):sock.sendto(json.dumps(dict(protocol=1,**values)).encode(),('127.0.0.1',55370))
        def until(predicate):
            for _ in range(400):
                f=frame()
                if predicate(f):return f
            raise AssertionError('Expected state not reached')
        try:
            first=frame();assert not upstream or first['upstream_steering'];request(land_hold=True)
            held=until(lambda f:f['perch_phase']=='perched')
            request(land_hold=True,rider_attached=False,autonomous_idle=True)
            unloaded=until(lambda f:not f['rider_attached']);start=unloaded['sim_time'];origin=unloaded['anchor_position']
            walking=until(lambda f:f['idle_behavior']=='walking');assert walking['rider_load']['mass_fraction']==0
            rested=until(lambda f:f['perch_phase']=='perched' and f['sim_time']>walking['sim_time']+.15)
            assert rested['contacting_claws']>=3
            displacement=sum((a-b)**2 for a,b in zip(rested['anchor_position'],origin))**.5
            assert .0001<displacement<.0035
            request(land_hold=True,rider_attached=False,autonomous_idle=False,paused=True)
            paused=until(lambda f:f['paused']);same=frame()
            assert same['sim_time']==paused['sim_time'] and same['neural_time']==paused['neural_time']
            assert same['positions']==paused['positions'] and same['rotations']==paused['rotations']
            request(land_hold=True,rider_attached=True,autonomous_idle=False,paused=False)
            restored=until(lambda f:f['rider_attached']);assert abs(restored['rider_load']['mass_fraction']-1/3)<1e-12
            assert restored['sim_time']>=rested['sim_time']
            if upstream:
                request(reset=True,land_hold=False,rider_attached=True,autonomous_idle=False,paused=False)
                reset=until(lambda f:f['session']!=restored['session'])
                assert reset['sim_time']<.1 and reset['neural_time']<.1
                assert reset['rider_load']['mass_fraction']==1/3 and reset['upstream_steering']
            result=dict(qualified=True,neurons=138639,continuous_brain_body_clocks=True,
                unloaded_live_idle_gait=True,excursion_m=displacement,landed_claws=rested['contacting_claws'],
                pause_passed=True,remount_passed=True,reset_passed=upstream,trained_head_stabilization=head,upstream_steering=upstream,biological_decision_making=False)
            Path(__file__).with_name('upstream-idle-stream-evaluation.json' if upstream else 'head-idle-stream-evaluation.json' if head else 'walking-idle-stream-evaluation.json').write_text(json.dumps(result,indent=2));print(result,flush=True)
        finally:
            process.terminate();process.wait(timeout=10)
if __name__=='__main__':
    import argparse
    parser=argparse.ArgumentParser();parser.add_argument('--head-stabilization',action='store_true')
    parser.add_argument('--upstream-steering',action='store_true');args=parser.parse_args()
    main(args.head_stabilization,args.upstream_steering)
