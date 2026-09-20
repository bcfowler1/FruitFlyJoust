"""Engineering contact-load allocation through native leg position actuators.

Desired support forces are converted to joint motor targets. No external wrench
or physical state assignment is made; actual contact dynamics must validate it.
"""
import mujoco
import numpy as np


def support_targets(physics,ik,anchor,feet,height,load_fraction):
    m=physics.model;d=physics.data
    supports=[(g,e) for g,e,_ in ik.legs if m.id2name(g,'geom') in feet]
    if not supports:return {},dict(supports=0,clipped_motor_targets=0)
    mass=float(m.body_subtreemass[anchor]);weight=mass*abs(float(m.opt.gravity[2]))
    velocity=np.zeros(6)
    mujoco.mj_objectVelocity(m.ptr,d.ptr,mujoco.mjtObj.mjOBJ_BODY,anchor,velocity,0)
    up=d.xmat[anchor].reshape(3,3)[:,2]
    # Preserve the approach behavior. Once all six claws actually support the
    # body, increase only the native-motor attitude correction for the hold.
    stiffness=weight*(.1 if len(supports)==6 else .05);inertia=mass*.06**2
    damping=2*np.sqrt(stiffness*inertia)
    torque=stiffness*np.array([up[1],-up[0],0])-damping*velocity[:3]
    vertical_stiffness=weight/.2;vertical_damping=2*np.sqrt(mass*vertical_stiffness)
    vertical=np.clip(weight+vertical_stiffness*(height-float(d.xpos[anchor,2]))-vertical_damping*velocity[5],.3*weight,1.5*weight)
    force=np.array([-.05*velocity[3],-.05*velocity[4],vertical*load_fraction])
    desired=np.r_[force,torque]
    blocks=[]
    for geom,entries in supports:
        x,y,z=d.geom_xpos[geom]-d.subtree_com[anchor]
        blocks.append(np.vstack((np.eye(3),np.array([[0,-z,y],[z,0,-x],[-y,x,0]]))))
    allocation=np.concatenate(blocks,axis=1)
    reactions=(allocation.T@np.linalg.solve(allocation@allocation.T+np.eye(6)*1e-5,desired)).reshape(-1,3)
    targets={};clipped=0
    for (geom,entries),reaction in zip(supports,reactions):
        # A claw cannot pull more than its existing native adhesion strength.
        suffix=m.id2name(geom,'geom').split('tarsal_claw_')[-1].removesuffix('_collision')
        adhesion=m.name2id('walker/adhere_claw_'+suffix,'actuator')
        strength=float(m.actuator_gainprm[adhesion,0])
        reaction[2]=np.clip(reaction[2],-.8*strength,2*weight)
        shear=max(0.,reaction[2])+strength
        reaction[:2]=np.clip(reaction[:2],-shear,shear)
        mujoco.mj_jacGeom(m.ptr,d.ptr,ik.jac,ik.rot,geom)
        torques=-ik.jac[:,[e[0] for e in entries]].T@reaction
        for (dof,qpos,aid,jid),joint_torque in zip(entries,torques):
            gain=float(m.actuator_gainprm[aid,0])
            # Account for native limb gravity/Coriolis bias and passive spring
            # load. Retain the native viscous damping rather than cancelling it.
            spring=float(m.jnt_stiffness[jid]*(m.qpos_spring[qpos]-d.qpos[qpos]))
            motor_torque=float(d.qfrc_bias[dof]+joint_torque-spring)
            target=float(d.qpos[qpos]+motor_torque/gain)
            if m.actuator_ctrllimited[aid]:
                limited=float(np.clip(target,*m.actuator_ctrlrange[aid]));clipped+=int(limited!=target);target=limited
            targets[aid]=target
    return targets,dict(supports=len(supports),clipped_motor_targets=clipped,
        desired_support_wrench=desired.tolist(),allocated_contact_reactions=reactions.tolist(),
        allocation_residual=float(np.linalg.norm(allocation@reactions.ravel()-desired)),
        native_adhesion_strength_preserved=True,native_motor_limits_preserved=True,no_external_wrenches=True)
