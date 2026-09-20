"""Fast empirical translation lab; no live brain or wing-resolved force integration."""
import argparse,json,time,socket,uuid
from pathlib import Path
import mujoco,numpy as np
from serve_flight import create_environment,flight_geoms,state_packet
from averaged_flight import AveragedTranslation
from rider_load import RiderLoad

def main():
 p=argparse.ArgumentParser();p.add_argument('--duration',type=float,default=0);p.add_argument('--input-port',type=int,default=55370);p.add_argument('--output-port',type=int,default=55371);args=p.parse_args()
 calibration=json.loads(Path(__file__).with_name('averaged-flight-calibration.json').read_text())
 env=create_environment(1);env.reset();load=RiderLoad(env.physics).apply(env.physics)
 m,d=env.physics.model.ptr,env.physics.data.ptr;anchor=mujoco.mj_name2id(m,mujoco.mjtObj.mjOBJ_BODY,'walker/thorax');geoms=flight_geoms(m)
 base=env.physics.data.qpos.copy();paused=False;turn=climb=0.;pending_spur=pending_brake=False
 response=None;cruise=burst=yaw=sim=0.;session='';seq=0
 def reset():
  nonlocal response,cruise,burst,yaw,session,seq,sim
  env.physics.data.qpos[:]=base;env.physics.data.qvel[:]=0;env.physics.data.time=0;env.physics.forward()
  response=AveragedTranslation(calibration['coefficients'],(.15,0,0),(.15,0,0));cruise=.15;burst=0.;yaw=0.;session=uuid.uuid4().hex;seq=0;sim=0.
 reset()
 with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as sock:
  sock.bind(('127.0.0.1',args.input_port));sock.setblocking(False);start=time.perf_counter();next_tick=start;last_wall=start-.015
  print('FAST_FLIGHT_READY: empirical loaded translation; no connectome; yaw and rein mappings uncalibrated',flush=True)
  while not args.duration or time.perf_counter()-start<args.duration:
   while True:
    try:raw,peer=sock.recvfrom(4096)
    except BlockingIOError:break
    except ConnectionResetError:continue
    if peer[0]!='127.0.0.1':continue
    try:
     request=json.loads(raw)
     if request.get('protocol')!=1:continue
     requested=np.asarray([request.get('turn',0),request.get('climb',0)],dtype=float)
     if not np.isfinite(requested).all() or np.max(np.abs(requested))>1:continue
     turn,climb=requested
     pending_spur|=request.get('spur_press') is True;pending_brake|=request.get('brake_press') is True
     if type(request.get('paused')) is bool:paused=request['paused']
     if request.get('reset') is True:reset();pending_spur=pending_brake=False
    except (ValueError,TypeError,AttributeError):continue
   tick=time.perf_counter()
   if not paused:
    if pending_brake:cruise=max(.03,cruise-.02);burst=0
    elif pending_spur:cruise=min(.3,cruise+.02);burst=.06
    pending_spur=pending_brake=False
    yaw+=turn*2*.015;speed=cruise+burst;burst*=np.exp(-.015/.15)
    target=np.array([np.cos(yaw)*speed,np.sin(yaw)*speed,.04*climb])
    velocity=response.step(target);env.physics.data.qpos[:3]+=velocity*.015*100
    yaw_quat=np.array([np.cos(yaw/2),0,0,np.sin(yaw/2)]);orientation=np.empty(4);mujoco.mju_mulQuat(orientation,yaw_quat,base[3:7]);env.physics.data.qpos[3:7]=orientation
    env.physics.data.qvel[:3]=velocity*100;sim+=.015;env.physics.data.time=sim;env.physics.forward()
   frame=state_packet(env,geoms,anchor,session,seq,paused,False,time.perf_counter()-tick,0 if paused else .015,0)
   frame.update(fast_averaged=True,body_controller='EMPIRICAL TRANSLATION; engineering yaw/reins; fixed wing pose; no brain',rider_load=load,calibration_rmse_mps=calibration['tests'][-1]['velocity_rmse_mps'])
   frame['compute_ratio']=0 if paused else .015/max(1e-6,tick-last_wall);last_wall=tick
   sock.sendto(json.dumps(frame,separators=(',',':')).encode(),('127.0.0.1',args.output_port));seq+=1
   next_tick+=.015;time.sleep(max(0,next_tick-time.perf_counter()))
 env.close()
if __name__=='__main__':main()
