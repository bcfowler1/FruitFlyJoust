"""Exercise the actual qualified stream, exact pause, completed loop and replay."""
import argparse
from datetime import datetime,timezone
import json
import math
from pathlib import Path
import socket
import subprocess
import sys
import time


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--qualification',type=Path,required=True)
    args=parser.parse_args();root=Path(__file__).resolve().parent
    prefix='unified-stream-'+datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
    report=root/(prefix+'-evaluation.json');log=root/(prefix+'.log')
    result=dict(qualified=False,qualification=args.qualification.name,log=log.name)
    input_port,output_port=55470,55471
    with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as receiver,log.open('x',encoding='utf-8') as output:
        receiver.bind(('127.0.0.1',output_port));receiver.settimeout(.3)
        process=subprocess.Popen([sys.executable,str(root/'serve_unified.py'),'--qualification',str(args.qualification.resolve()),
            '--input-port',str(input_port),'--output-port',str(output_port)],stdout=output,stderr=subprocess.STDOUT,
            creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
        def request(**values):
            receiver.sendto(json.dumps(dict(protocol=1,**values)).encode(),('127.0.0.1',input_port))
        start=time.monotonic();pause_start=None;pause_frame=None;session=None;previous=None
        mode='starting';frames=0;max_gap=0;completed=False;replayed=False
        try:
            while time.monotonic()-start<240:
                if process.poll() is not None:raise RuntimeError('Backend exited; inspect '+log.name)
                try:raw,_=receiver.recvfrom(65507)
                except socket.timeout:continue
                frame=json.loads(raw);frames+=1
                assert frame['protocol']==1 and frame['neurons']==0 and frame['neural_time']==0
                assert len(frame['positions'])>0 and len(frame['positions'])%3==0
                assert len(frame['rotations'])==len(frame['positions'])//3*4
                assert all(math.isfinite(x) for key in ('positions','rotations','anchor_position','anchor_rotation') for x in frame[key])
                if session is None:session=frame['session']
                if previous and frame['session']==previous['session']:
                    assert frame['seq']>previous['seq'] and frame['sim_time']>=previous['sim_time']
                    max_gap=max(max_gap,frame['sim_time']-previous['sim_time'])
                previous=frame
                if mode=='starting' and frame['sim_time']>=.08:
                    request(paused=True);mode='pausing'
                elif mode=='pausing' and frame['paused']:
                    pause_frame=frame;pause_start=time.monotonic();mode='paused'
                elif mode=='paused':
                    assert frame['paused'] and frame['sim_time']==pause_frame['sim_time']
                    assert all(frame[key]==pause_frame[key] for key in ('positions','rotations','anchor_position','anchor_rotation'))
                    if time.monotonic()-pause_start>=1:
                        request(paused=False);mode='resumed'
                elif mode=='resumed' and frame['episode_ended']:
                    assert frame['episode_status']=='complete: qualification passed'
                    completed=True;request(reset=True,paused=False);mode='replaying'
                elif mode=='replaying' and frame['session']!=session:
                    assert frame['sim_time']<.1
                    replayed=True;request(paused=True);break
            assert completed and replayed and pause_frame is not None
            assert max_gap<.035, 'Copied forecast or body-clock discontinuity leaked into stream'
            output.flush()
            trials=[]
            for line in log.read_text(encoding='utf-8').splitlines():
                try:event=json.loads(line)
                except ValueError:continue
                if event.get('stage')=='live_trial_finished':trials.append(event)
            assert len(trials)==1 and trials[0]['qualified']
            actual=json.loads((root/trials[0]['report']).read_text())
            assert actual['qualified'] and actual['walking_resume']['completed_walking_seconds']==6
            assert actual['root_external_force'] is False and actual['pose_handover_jump']==0 and actual['velocity_handover_jump']==0
            assert actual['walking_resume']['time_reset'] is False and abs(actual['rider_load']['mass_fraction']-1/3)<1e-12
            result.update(qualified=True,frames=frames,exact_paused_geometry_and_clock=True,
                paused_wall_seconds=1,resumed=True,completed_physical_trial=trials[0]['report'],
                explicit_replay_new_session=True,maximum_stream_time_gap=max_gap,
                limitations='Automated engineering floor trajectory, camera/pause/replay only; no live rein or neural motor qualification.')
        except Exception as error:
            result['error']=repr(error)
        finally:
            process.terminate();process.wait(timeout=10)
            report.write_text(json.dumps(result,indent=2));print(json.dumps(dict(report=report.name,**result)),flush=True)
    return 0 if result['qualified'] else 1


if __name__=='__main__':sys.exit(main())
