"""Stream the qualified higher-landing same-body floor loop; no live rein/brain mapping.

Transport observes the evaluator's real physics only. Copied predictions are
never streamed. An explicit replay creates a new trial; pause never advances it.
"""
import argparse
import hashlib
import json
from pathlib import Path
import socket
import time
import uuid

import mujoco
from dm_control import mjcf
import evaluate_cartesian_walking_transition as evaluation
from export_body import export_geometry, visible_geoms
from landing_contacts import claw_contacts

np=evaluation.np
ROOT=Path(__file__).resolve().parent


class Replay(Exception):
    pass


def qualification(path):
    result=json.loads(path.read_text())
    experiments=result.get('experiments',[])
    seeds={row.get('seed') for row in experiments if row.get('qualified')}
    if not result.get('qualified') or len(experiments)<3 or len(seeds)<3 or not all(row.get('qualified') for row in experiments):
        raise RuntimeError('Requires the completed three-seed higher-landing qualification')
    for name,digest in result['source_sha256'].items():
        if hashlib.sha256((ROOT/name).read_bytes()).hexdigest()!=digest:
            raise RuntimeError('Source changed since higher-landing qualification: '+name)
    return result


class Stream:
    def __init__(self,sock,port):
        self.sock=sock;self.port=port;self.physics=None;self.paused=False
        self.ended=False;self.phase='initializing';self.session=uuid.uuid4().hex
        self.seq=0;self.next_frame=0;self.geometry_exported=False
        self.walking_start=None
    def bind(self,physics):
        # Composer exposes a weak proxy. A bound method retains the canonical
        # Physics object and gives identity checks the same object as step().
        self.physics=physics.step.__self__;m=self.physics.model.ptr
        self.geoms=[i for i in visible_geoms(m) if (physics.model.id2name(i,'geom') or '').startswith('walker/')]
        self.anchor=physics.model.name2id('walker/thorax','body')
        if not self.geometry_exported:
            export_geometry(m,self.geoms,ROOT.parent/'Assets/Resources/FlyUnifiedGeometry.bytes')
            self.geometry_exported=True
        self.phase='walking before launch'
    def requests(self):
        while True:
            try:raw,peer=self.sock.recvfrom(4096)
            except BlockingIOError:break
            except ConnectionResetError:continue
            if peer[0]!='127.0.0.1':continue
            try:
                request=json.loads(raw)
                if request.get('protocol')!=1:continue
                if isinstance(request.get('paused'),bool):self.paused=request['paused']
                if request.get('reset') is True:raise Replay()
            except (ValueError,TypeError,AttributeError):continue
    def frame(self):
        if self.physics is None:return
        d=self.physics.data;rotations=[]
        for geom in self.geoms:
            quat=np.empty(4);mujoco.mju_mat2Quat(quat,d.geom_xmat[geom])
            rotations.extend(quat.tolist())
        feet,other=claw_contacts(self.physics.model,d)
        packet=dict(protocol=1,session=self.session,seq=self.seq,sim_time=float(d.time),
            neural_time=0,neurons=0,connections=0,spikes=0,active=0,rate_hz=0,
            paused=self.paused or self.ended,episode_ended=self.ended,episode_status=self.phase,
            positions=(d.geom_xpos[self.geoms]*.01).ravel().tolist(),rotations=rotations,
            anchor_position=(d.xpos[self.anchor]*.01).tolist(),anchor_rotation=d.xquat[self.anchor].tolist(),
            contacting_claws=len(feet),body_contacts=other,
            body_controller='Qualified higher landing, engineering stance bridge, native walking policy; no connectome limb mapping',
            rider_cue_mapping='Camera/pause/replay only; automated higher-landing qualification')
        self.sock.sendto(json.dumps(packet,separators=(',',':')).encode(),('127.0.0.1',self.port));self.seq+=1
    def before_step(self):
        self.requests()
        while self.paused:
            self.frame();time.sleep(.015);self.requests()
    def after_step(self):
        if self.walking_start is not None and self.physics.data.time>=self.walking_start:
            self.phase='walking resumed'
        if self.phase in ('walking before launch','standing before launch'):
            if self.physics.data.time>=.7:self.phase='wing takeoff and hover'
            elif self.physics.data.time>=.4:self.phase='standing before launch'
        if self.physics.data.time>=self.next_frame:
            self.frame();self.next_frame=float(self.physics.data.time)+.015


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--qualification',type=Path,required=True)
    parser.add_argument('--seed',type=int,default=42)
    parser.add_argument('--input-port',type=int,default=55370)
    parser.add_argument('--output-port',type=int,default=55371)
    args=parser.parse_args();qualified=qualification(args.qualification)
    if args.seed not in {row['seed'] for row in qualified['experiments']}:parser.error('Seed was not qualified')
    step=mjcf.Physics.step;apply=evaluation.RiderLoad.apply;plain_dumps=json.dumps
    def numpy_dumps(value,*arguments,**keywords):
        keywords.setdefault('default',lambda item:item.tolist() if hasattr(item,'tolist') else float(item))
        return plain_dumps(value,*arguments,**keywords)
    with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as sock:
        sock.bind(('127.0.0.1',args.input_port));sock.setblocking(False)
        while True:
            stream=Stream(sock,args.output_port)
            def loaded(load,physics):
                result=apply(load,physics);stream.bind(physics);return result
            def observed_step(physics,*arguments,**keywords):
                real=physics is stream.physics
                if real:stream.before_step()
                result=step(physics,*arguments,**keywords)
                if real:stream.after_step()
                return result
            def stage_print(*values,**kwargs):
                if values and isinstance(values[0],str):
                    try:event=json.loads(values[0])
                    except ValueError:event={}
                    if 'stage' in event:stream.phase=event['stage'].replace('_',' ')
                    if event.get('stage')=='cartesian_walking_manifold_transition' and event.get('qualified'):
                        stream.phase='walking handoff'
                        stream.walking_start=float(stream.physics.data.time)+.1
                print(*values,**kwargs)
            report='higher-landing-live-'+stream.session+'.json'
            try:
                mjcf.Physics.step=observed_step;evaluation.RiderLoad.apply=loaded;evaluation.print=stage_print
                evaluation.json.dumps=numpy_dumps
                evaluation.main(launch_height=.25,output_name=report,landing=True,upright_landing=False,
                    stance_before_launch=True,leg_ik=True,claw_hold=True,resume_walking=True,
                    seed=args.seed,grip_ramp=.2,stance_ramp=.02,hold_seconds=2,
                    filter_settle_seconds=.1,claw_release_seconds=.25,walking_seconds=6,
                    match_resume_height=True,resume_speed=1,grip_max_speed=2,
                    adaptive_grip=True,loaded_stance_handover=True,
                    synchronize_wings=True,complete_contacts=True,walking_blend_seconds=.25)
                passed=json.loads((ROOT/report).read_text())['qualified']
                stream.ended=True;stream.phase='complete: qualification passed' if passed else 'FAILED: see saved evaluation'
                print(json.dumps(dict(stage='live_trial_finished',qualified=passed,report=report)),flush=True)
                while True:
                    stream.requests();stream.frame();time.sleep(.015)
            except Replay:
                continue
            finally:
                mjcf.Physics.step=step;evaluation.RiderLoad.apply=apply;evaluation.json.dumps=plain_dumps


if __name__=='__main__':main()
