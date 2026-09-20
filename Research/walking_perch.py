"""Same-body gait/contact/engineering-hover handover; no state transplantation."""
import numpy as np,mujoco
from flygym.simulation import SingleFlySimulation

class WalkingPerch:
    def __init__(self,body):
        self.body=body;self.phase='walking';self.stable_time=0.;self.strength=1.
        self.targets=None;self.origin=None;self.orientation=None;self.goal=None
        self.mass_matrix=np.empty((body.m.nv,body.m.nv))
        self.approach_elapsed=0.
        self.verify_from='settling'
        self.lost_time=0.
    def land(self):
        b=self.body
        if self.phase in ('walking','flying','launching'):
            self.targets=b.sim.physics.bind(b.fly.actuators).ctrl.copy()
            if self.phase=='walking':self.phase='settling'
            else:
                self.phase='approaching';self.goal=self.origin.copy()
            self.stable_time=0.
            self.approach_elapsed=0.
    def launch(self):
        if self.phase!='perched':return
        b=self.body;self.origin=b.d.qpos[:3].copy();self.orientation=b.d.qpos[3:7].copy()
        self.goal=self.origin+np.array([0,0,.75]);self.phase='launching';self.stable_time=0.
    def resume_walk(self):
        if self.phase!='perched':return
        self.phase='walking';self.body.d.xfrc_applied[:]=0;self.body.d.qfrc_applied[:]=0
    def step(self,action):
        b=self.body;m,d=b.m,b.d
        if self.phase=='walking':b.step(action);return
        for _ in range(150):
            d.xfrc_applied[:]=0
            d.qfrc_applied[:]=0
            if self.phase=='approaching':
                self.approach_elapsed+=.0001
                if self.approach_elapsed>.15 and len(b.contacts())<3:
                    # Search up to 0.25 mm toward the actual flat surface.
                    # The root remains physics-integrated; only the force goal moves.
                    self.goal[2]=max(self.origin[2]-.25,self.goal[2]-.5*.0001)
            if self.phase in ('launching','flying','approaching'):
                error=np.empty(3);mujoco.mju_subQuat(error,self.orientation,d.qpos[3:7])
                acceleration=np.concatenate((10000*(self.goal-d.qpos[:3])-200*d.qvel[:3],10000*error-200*d.qvel[3:6]))
                # Full loaded root inertia and gravity/coriolis compensation;
                # applied generalized wrench, never position or velocity assignment.
                mujoco.mj_fullM(m,self.mass_matrix,d.qM)
                d.qfrc_applied[:6]=d.qfrc_bias[:6]+self.mass_matrix[:6,:6]@acceleration
            adhesion=self.strength if self.phase in ('settling','approaching','verifying','perched') else 0.
            b.obs,_,terminated,truncated,_=SingleFlySimulation.step(b.sim,
                dict(joints=self.targets,adhesion=np.ones(6)*adhesion))
            b.ended=bool(terminated or truncated)
            if not np.isfinite(d.qpos).all() or not np.isfinite(d.qvel).all():raise RuntimeError('Nonfinite perch state')
            contacts=len(b.contacts())
            if self.phase in ('settling','approaching'):
                stable=contacts>=3 and np.linalg.norm(d.qvel[:3])<2
                self.stable_time=self.stable_time+.0001 if stable else 0.
                if self.stable_time>=.03:
                    self.verify_from=self.phase;self.phase='verifying';self.stable_time=0.;self.lost_time=0.
                    d.xfrc_applied[:]=0;d.qfrc_applied[:]=0
            elif self.phase=='verifying':
                self.lost_time=self.lost_time+.0001 if contacts<3 else 0.
                if self.lost_time>=.03:
                    self.phase=self.verify_from;self.stable_time=0.
                    if self.phase=='approaching':self.goal[2]=max(self.origin[2]-.25,self.goal[2]-.01)
                else:
                    self.stable_time+=.0001
                    if self.stable_time>=.15 and contacts>=3 and np.linalg.norm(d.qvel[:3])<2:self.phase='perched'
            elif self.phase=='perched' and contacts<3:
                self.phase='settling';self.stable_time=0.
            elif self.phase=='launching' and contacts==0 and d.qpos[2]-self.origin[2]>.6:
                self.phase='flying'
            if b.ended:break
