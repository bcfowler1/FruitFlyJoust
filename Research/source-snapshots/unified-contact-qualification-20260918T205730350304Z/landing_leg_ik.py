"""Experimental physical leg targets for a flat floor; no root forces or pose edits."""
import mujoco,numpy as np
class LandingLegIK:
    def __init__(self,physics,walker,anchor,posture_gain=0):
        self.physics=physics;self.anchor=anchor;self.legs=[]
        m=physics.model;d=physics.data
        self.initial_rotation=d.xmat[anchor].reshape(3,3).copy()
        self.recovery_rotation=None
        self.body_recovery=None
        self.swing_goal=None
        self.initial_height=float(d.xpos[anchor,2])
        self.posture_gain=posture_gain;self.initial_angles={}
        for segment in ('T1','T2','T3'):
            for side in ('left','right'):
                suffix=segment+'_'+side
                geom=m.name2id('walker/tarsal_claw_'+suffix+'_collision','geom')
                joints=[j for j in walker.mjcf_model.find_all('joint') if j.name.endswith(suffix)]
                entries=[]
                for j in joints:
                    actuator=walker.mjcf_model.find('actuator',j.name)
                    if actuator is None:continue
                    jid=m.name2id(j.full_identifier,'joint');aid=m.name2id(actuator.full_identifier,'actuator')
                    entries.append((int(m.jnt_dofadr[jid]),int(m.jnt_qposadr[jid]),aid,jid))
                    self.initial_angles[aid]=float(d.qpos[int(m.jnt_qposadr[jid])])
                self.legs.append((geom,entries,d.geom_xpos[geom].copy()-d.xpos[anchor].copy()))
        self.jac=np.zeros((3,m.nv));self.rot=np.zeros((3,m.nv));self.targets={}
    def begin_body_recovery(self,height=None,gain=.1,maximum_step=.001):
        d=self.physics.data;matrix=d.xmat[self.anchor].reshape(3,3)
        yaw=np.arctan2(matrix[1,0],matrix[0,0]);c,s=np.cos(yaw),np.sin(yaw)
        desired_rotation=np.array([[c,-s,0],[s,c,0],[0,0,1]])
        origin=d.xpos[self.anchor].copy();desired_origin=origin.copy();desired_origin[2]=self.initial_height if height is None else height
        self.recovery_target_height=float(desired_origin[2])
        self.recovery_gain=gain;self.recovery_maximum_step=maximum_step
        local_goals={geom:desired_rotation.T@(d.geom_xpos[geom].copy()-desired_origin) for geom,_,_ in self.legs}
        self.body_recovery=local_goals
        # Redundant leg joints can curl into policy-incompatible poses while
        # meeting foot targets. Recover measured preflight posture only in
        # the foot Jacobian null space; do not move the root or fixed claws.
        self.posture_gain=0
        self.support_bias={aid:float(d.ctrl[aid]-d.qpos[qpos]) for _,entries,_ in self.legs for _,qpos,aid,_ in entries}
        self.targets={aid:float(d.ctrl[aid]) for _,entries,_ in self.legs for _,_,aid,_ in entries}
    def update(self):
        m=self.physics.model;d=self.physics.data
        for geom,entries,offset in self.legs:
            if self.recovery_rotation is not None:
                offset=self.recovery_rotation@self.initial_rotation.T@offset
            goal=d.xpos[self.anchor].copy()+offset;goal[2]=float(m.geom_size[geom,0])+.001
            if self.body_recovery is not None:
                goal=d.xpos[self.anchor]+d.xmat[self.anchor].reshape(3,3)@self.body_recovery[geom]
            if self.swing_goal is not None and self.swing_goal[0]==geom:
                goal=self.swing_goal[1]
            mujoco.mj_jacGeom(m.ptr,d.ptr,self.jac,self.rot,geom)
            dofs=[e[0] for e in entries];jac=self.jac[:,dofs]
            error=np.clip(goal-d.geom_xpos[geom],-.02,.02)
            correction=jac.T@np.linalg.solve(jac@jac.T+np.eye(3)*.0001,error)
            if self.posture_gain:
                inverse=jac.T@np.linalg.solve(jac@jac.T+np.eye(3)*.0001,np.eye(3))
                posture=np.array([self.initial_angles[e[2]]-d.qpos[e[1]] for e in entries])
                correction+=self.posture_gain*(np.eye(len(entries))-inverse@jac)@posture
            for (_,qpos,actuator,joint),delta in zip(entries,correction):
                target=float(self.targets.get(actuator,d.ctrl[actuator])+np.clip(delta*self.recovery_gain,-self.recovery_maximum_step,self.recovery_maximum_step)) if self.body_recovery is not None else float(d.qpos[qpos]+np.clip(delta,-.05,.05))
                if self.swing_goal is not None and self.swing_goal[0]==geom:
                    # An ungripped swing foot has no static support load to
                    # preserve; use the original position IK for placement.
                    target=float(d.qpos[qpos]+np.clip(delta,-.05,.05))
                if m.jnt_limited[joint]:target=float(np.clip(target,*m.jnt_range[joint]))
                self.targets[actuator]=target
    def apply(self):
        for actuator,target in self.targets.items():self.physics.data.ctrl[actuator]=target
