"""Engineering approach/launch controller with actual full-body claw contact.
Averaged external wrench supplies flight; no wing-policy or neural validation.
"""
import json
from pathlib import Path
import mujoco,numpy as np
from export_body import body_model
from adhesion_lab import physics_wrapper,claw_contacts
from rider_load import RiderLoad

class PerchTransition:
 def __init__(self,surface='wall'):
  self.model,self.data=body_model();m,d=self.model,self.data
  self.load=RiderLoad(physics_wrapper(m,d)).apply(physics_wrapper(m,d))
  self.anchor=mujoco.mj_name2id(m,mujoco.mjtObj.mjOBJ_BODY,'thorax')
  self.actuators=[i for i in range(m.nu) if (mujoco.mj_id2name(m,mujoco.mjtObj.mjOBJ_ACTUATOR,i) or '').startswith('adhere_claw')]
  for _ in range(3000):mujoco.mj_step(m,d)
  self.stand=d.qpos[:3].copy();self.orientation=d.qpos[3:7].copy();self.inertia=m.body_inertia[self.anchor].copy()
  self.mass=float(m.body_subtreemass[self.anchor]);m.opt.gravity[:]=dict(floor=(0,0,-981),wall=(-981,0,0),ceiling=(0,0,981))[surface]
  d.qpos[2]+=.15;d.qvel[:]=0;d.time=0;mujoco.mj_forward(m,d)
  self.phase='flying';self.contact_time=0.;self.strength=1.;self.goal=self.stand+np.array([0,0,.15])
  self.base_orientation=self.orientation.copy();self.yaw=0.
 def cruise(self,turn,climb,speed,dt=.015):
  """Engineering surface-relative cruise; poses remain entirely physics-integrated."""
  if self.phase!='flying':return
  values=np.asarray([turn,climb,speed,dt],dtype=float)
  if not np.isfinite(values).all() or abs(turn)>1 or abs(climb)>1 or not 0<=speed<=.3 or not 0<dt<=.015:raise ValueError('Invalid cruise cue')
  self.yaw+=turn*dt*2
  rotation=np.array([np.cos(self.yaw/2),0,0,np.sin(self.yaw/2)])
  mujoco.mju_mulQuat(self.orientation,rotation,self.base_orientation)
  self.goal[:2]+=np.array([np.cos(self.yaw),np.sin(self.yaw)])*speed*100*dt
  self.goal[2]=np.clip(self.goal[2]+climb*.04*100*dt,self.stand[2]+.1,self.stand[2]+.3)
 def land(self):
  if self.phase!='perched':
   self.stand[:2]=self.data.qpos[:2];self.orientation[:]=self.base_orientation
   self.phase='approaching';self.goal=self.stand-np.array([0,0,.05])
 def launch(self):
  self.phase='launching';self.goal=self.stand+np.array([0,0,.15]);self.contact_time=0
 def step(self):
  m,d=self.model,self.data;dt=m.opt.timestep
  contacts=len(claw_contacts(m,d));d.ctrl[self.actuators]=self.strength if self.phase in ('approaching','perched') else 0
  d.xfrc_applied[:]=0
  if self.phase=='perched' and contacts<3:self.launch()
  if self.phase!='perched':
   acceleration=900*(self.goal-d.qpos[:3])-60*d.qvel[:3]-m.opt.gravity
   d.xfrc_applied[self.anchor,:3]=self.mass*acceleration
   error=np.empty(3);mujoco.mju_subQuat(error,self.orientation,d.qpos[3:7])
   rotation=d.xmat[self.anchor].reshape(3,3)
   correction=rotation@(1e-5*(1600*error-80*d.qvel[3:6]))
   lever=d.subtree_com[self.anchor]-d.xipos[self.anchor]
   d.xfrc_applied[self.anchor,3:]=correction+np.cross(lever,d.xfrc_applied[self.anchor,:3])
   if self.phase=='approaching':
    self.contact_time=self.contact_time+dt if contacts>=4 and self.strength>0 and np.linalg.norm(d.qvel[:6])<.5 else 0
    if self.contact_time>=.02:self.phase='perched';d.xfrc_applied[:]=0
   elif self.phase=='launching' and contacts==0 and d.qpos[2]-self.stand[2]>.1:self.phase='flying'
  mujoco.mj_step(m,d)
  if not np.isfinite(d.qpos).all():raise RuntimeError('Non-finite transition')

def main():
 trials=[]
 for surface in ('floor','wall','ceiling'):
  c=PerchTransition(surface);history=[]
  for _ in range(3000):c.step()
  initial_contacts=len(claw_contacts(c.model,c.data));c.land()
  for _ in range(12000):
   c.step();history.append(c.phase)
   if c.phase=='perched':break
  landed=c.phase=='perched';landing_time=float(c.data.time)
  for _ in range(3000):c.step()
  held=c.phase=='perched' and len(claw_contacts(c.model,c.data))>=3
  held_claws=len(claw_contacts(c.model,c.data))
  c.launch()
  for _ in range(12000):
   c.step()
   if c.phase=='flying':break
  result=dict(surface=surface,initial_airborne=initial_contacts==0,landed=landed,held=held,held_claws=held_claws,launched=c.phase=='flying',final_claws=len(claw_contacts(c.model,c.data)),landing_time=landing_time,finite=True)
  trials.append(result);print(json.dumps(result),flush=True)
 Path(__file__).with_name('perch-transition-evaluation.json').write_text(json.dumps(dict(trials=trials,engineering_controller=True,wing_resolved_flight=False,neural_control=False,root_welding=False,limits='Millimeter-scale flat-surface approach; synthetic averaged flight wrench, fixed leg targets and uncalibrated adhesion.'),indent=2))
if __name__=='__main__':main()
