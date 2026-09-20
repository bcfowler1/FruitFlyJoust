"""Engineering front-leg reach toward the nearest floor point below each hip."""
import numpy as np
import mujoco
from landing_leg_ik import LandingLegIK


class LandingReachIK(LandingLegIK):
    def update(self):
        super().update()
        if self.body_recovery is not None:return
        m=self.physics.model;d=self.physics.data
        lateral=d.xmat[self.anchor].reshape(3,3)[:,1].copy();lateral[2]=0
        lateral/=max(np.linalg.norm(lateral),1e-9)
        for geom,entries,_ in self.legs:
            name=m.id2name(geom,'geom')
            if 'T1_' not in name:continue
            suffix=name.split('tarsal_claw_')[-1].removesuffix('_collision')
            hip=m.name2id('walker/coxa_'+suffix,'body')
            sign=1 if suffix.endswith('left') else -1
            goal=d.xpos[hip].copy()+sign*.03*lateral
            clearance=float(m.geom_size[geom,0])
            if int(m.geom_type[geom])==int(mujoco.mjtGeom.mjGEOM_CAPSULE):
                clearance+=float(m.geom_size[geom,1])*abs(float(d.geom_xmat[geom].reshape(3,3)[2,2]))
            goal[2]=clearance-.0005
            mujoco.mj_jacGeom(m.ptr,d.ptr,self.jac,self.rot,geom)
            jac=self.jac[:,[e[0] for e in entries]]
            delta=jac.T@np.linalg.solve(jac@jac.T+np.eye(3)*.0001,np.clip(goal-d.geom_xpos[geom],-.02,.02))
            for (_,qpos,aid,jid),change in zip(entries,delta):
                target=float(d.qpos[qpos]+np.clip(change,-.05,.05))
                if m.jnt_limited[jid]:target=float(np.clip(target,*m.jnt_range[jid]))
                self.targets[aid]=target
