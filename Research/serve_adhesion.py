"""Interactive six-claw contact physics; no neural landing-control claim."""
import argparse,json,socket,time,uuid
import mujoco,numpy as np
from export_body import body_model,visible_geoms
from adhesion_lab import physics_wrapper,claw_contacts
from rider_load import RiderLoad

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--surface',choices=('floor','wall','ceiling'),default='wall')
    parser.add_argument('--transitions',action='store_true',help='Experimental averaged-wrench approach and launch')
    parser.add_argument('--continuous',action='store_true',help='Surface-relative engineering cruise in the same contact model')
    parser.add_argument('--duration',type=float,default=0)
    parser.add_argument('--input-port',type=int,default=55370)
    parser.add_argument('--output-port',type=int,default=55371)
    args=parser.parse_args()
    if args.continuous:args.transitions=True
    transition=None
    model,data=body_model(); geoms=visible_geoms(model)
    load=RiderLoad(physics_wrapper(model,data));load_report=load.apply(physics_wrapper(model,data))
    actuators=[i for i in range(model.nu) if (mujoco.mj_id2name(model,mujoco.mjtObj.mjOBJ_ACTUATOR,i) or '').startswith('adhere_claw')]
    def reset():
        nonlocal transition,model,data
        if args.transitions:
            from perch_transition import PerchTransition
            transition=PerchTransition(args.surface);model,data=transition.model,transition.data
            return
        mujoco.mj_resetData(model,data);model.opt.gravity[:]=(0,0,-981)
        for _ in range(3000):mujoco.mj_step(model,data)
        model.opt.gravity[:]=dict(floor=(0,0,-981),wall=(-981,0,0),ceiling=(0,0,981))[args.surface]
        data.ctrl[actuators]=1;data.time=0;mujoco.mj_forward(model,data)
    reset(); initial=data.qpos[:3].copy();ended=False
    anchor=mujoco.mj_name2id(model,mujoco.mjtObj.mjOBJ_BODY,'thorax')
    angle=dict(floor=0,wall=-np.pi/2,ceiling=np.pi)[args.surface]
    world_quat=np.array([np.cos(angle/2),0,np.sin(angle/2),0]);matrix=np.empty(9)
    mujoco.mju_quat2Mat(matrix,world_quat);matrix=matrix.reshape(3,3)
    session=uuid.uuid4().hex;seq=0;paused=False;active=True
    strength=1.
    land_before=False
    turn=climb=0.;cruise=.03;burst=0.
    with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as sock:
        sock.bind(('127.0.0.1',args.input_port));sock.setblocking(False);start=time.perf_counter()
        print('ADHESION_READY: six contact-only claw actuators, rigid miniature rider',flush=True)
        while not args.duration or time.perf_counter()-start<args.duration:
            while True:
                try:raw,peer=sock.recvfrom(4096)
                except (BlockingIOError,ConnectionResetError):break
                if peer[0]!='127.0.0.1':continue
                try:
                    request=json.loads(raw)
                    if request.get('protocol')!=1:continue
                    requested_strength=float(request.get('adhesion_strength',strength))
                    if not np.isfinite(requested_strength) or not 0 <= requested_strength <= 1:continue
                    strength=requested_strength
                    cues=np.asarray([request.get('turn',0),request.get('climb',0)],dtype=float)
                    if not np.isfinite(cues).all() or np.max(abs(cues))>1:continue
                    turn,climb=cues
                    if type(request.get('paused')) is bool:paused=request['paused']
                    if request.get('reset') is True:
                        reset();initial=data.qpos[:3].copy();ended=False;active=True;session=uuid.uuid4().hex;seq=0
                        land_before=False;cruise=.03;burst=0.
                    if request.get('spur_press') is True:
                        active=False
                        if transition:transition.launch()
                        cruise=min(.15,cruise+.01);burst=.02
                    if request.get('brake_press') is True:cruise=max(.005,cruise-.01);burst=0.
                    if request.get('land_hold') is True:
                        active=True
                        if transition and not land_before:transition.land()
                    land_before=request.get('land_hold') is True
                except (ValueError,AttributeError,TypeError):continue
            tick=time.perf_counter();data.ctrl[actuators]=strength if active else 0
            if not paused and not ended:
                if transition:transition.strength=strength
                if args.continuous:
                    transition.cruise(turn,climb,cruise+burst);burst*=np.exp(-.015/.15)
                for _ in range(150):
                    if transition:transition.step()
                    else:mujoco.mj_step(model,data)
                if transition:active=transition.phase in ('approaching','perched')
                ended=np.linalg.norm(data.qpos[:3]-initial)>(5 if args.continuous else .5)
            rotations=[]
            for i in geoms:
                quat=np.empty(4);world=np.empty(4);mujoco.mju_mat2Quat(quat,data.geom_xmat[i]);mujoco.mju_mulQuat(world,world_quat,quat)
                rotations.extend(world.tolist())
            orientation=np.empty(4);mujoco.mju_mulQuat(orientation,world_quat,data.xquat[anchor])
            packet=dict(protocol=1,session=session,seq=seq,sim_time=float(data.time),neural_time=0,neurons=0,
                connections=0,spikes=0,active=0,rate_hz=0,paused=paused,episode_ended=bool(ended),
                episode_status='excursion limit reached' if ended else 'running',
                compute_ratio=0 if paused or ended else .015/max(1e-6,time.perf_counter()-tick),
                positions=((data.geom_xpos[geoms]@matrix.T)*.01).ravel().tolist(),rotations=rotations,
                anchor_position=(matrix@data.xpos[anchor]*.01).tolist(),anchor_rotation=orientation.tolist(),
                surface_position=(matrix@np.array([0,0,-.132])*.01).tolist(),surface_normal=(matrix@np.array([0,0,1])).tolist(),
                contacting_claws=len(claw_contacts(model,data)),adhesion_enabled=active and strength>0,adhesion_strength=strength,rider_load=load_report,
                body_controller='SIX-CLAW CONTACT PHYSICS; uncalibrated adhesion, no neural landing controller')
            if not np.isfinite(data.qpos).all():raise RuntimeError('Non-finite contact state')
            if transition:
                packet['perch_phase']=transition.phase
                packet['body_controller']='EXPERIMENTAL averaged flight wrench + actual claw contact; no neural or wing-policy landing control'
                if args.continuous:packet['body_controller']='CONTINUOUS engineering cruise + real claws; yaw and flight wrench uncalibrated'
            sock.sendto(json.dumps(packet,separators=(',',':')).encode(),('127.0.0.1',args.output_port));seq+=1
            time.sleep(.03 if paused else .015)

if __name__=='__main__':main()
