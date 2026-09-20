"""Engineering stance IK in a virtual pose; the live body is never repositioned."""
import numpy as np
import mujoco


def grasp_targets(physics,ik,anchor,supported,loaded_targets):
    state={k:getattr(physics.data,k).copy() for k in ('qpos','qvel','act','ctrl')}
    clock=float(physics.data.time)
    trial=physics.copy(share_model=True)
    try:
        m=trial.model;d=trial.data
        matrix=physics.data.xmat[anchor].reshape(3,3)
        yaw=float(np.arctan2(matrix[1,0],matrix[0,0]));c,s=np.cos(yaw),np.sin(yaw)
        level=np.array([[c,-s,0],[s,c,0],[0,0,1]])
        positions={geom:physics.data.geom_xpos[geom].copy() for geom,_,_ in ik.legs}
        offsets={geom:level@ik.initial_rotation.T@offset for geom,_,offset in ik.legs}
        estimates=[positions[geom]-offsets[geom] for geom,_,_ in ik.legs if m.id2name(geom,'geom') in supported]
        origin=np.mean(estimates,axis=0);origin[2]=ik.initial_height
        # These pose assignments are confined to the kinematic model copy.
        # No copied trajectory is presented as a physical qualification.
        d.qpos[:3]=origin;d.qpos[3:7]=[np.cos(yaw/2),0,0,np.sin(yaw/2)]
        for _,entries,_ in ik.legs:
            for _,qpos,aid,_ in entries:d.qpos[qpos]=ik.initial_angles[aid]
        goals={geom:(positions[geom].copy() if m.id2name(geom,'geom') in supported else origin+offsets[geom]) for geom,_,_ in ik.legs}
        jac=np.zeros((3,m.nv));rot=np.zeros((3,m.nv))
        errors=[]
        for iteration in range(80):
            trial.forward();errors=[]
            for geom,entries,_ in ik.legs:
                clearance=float(m.geom_size[geom,0])
                if int(m.geom_type[geom])==int(mujoco.mjtGeom.mjGEOM_CAPSULE):
                    clearance+=float(m.geom_size[geom,1])*abs(float(d.geom_xmat[geom].reshape(3,3)[2,2]))
                goals[geom][2]=clearance-.0005
                error=goals[geom]-d.geom_xpos[geom];errors.append(float(np.linalg.norm(error)))
                mujoco.mj_jacGeom(m.ptr,d.ptr,jac,rot,geom)
                columns=jac[:,[e[0] for e in entries]]
                inverse=columns.T@np.linalg.solve(columns@columns.T+np.eye(3)*1e-5,np.eye(3))
                posture=np.array([ik.initial_angles[e[2]]-d.qpos[e[1]] for e in entries])
                delta=inverse@error+.01*(np.eye(len(entries))-inverse@columns)@posture
                for (_,qpos,aid,jid),change in zip(entries,delta):
                    value=float(d.qpos[qpos]+np.clip(change,-.1,.1))
                    if m.jnt_limited[jid]:value=float(np.clip(value,*m.jnt_range[jid]))
                    d.qpos[qpos]=value
            if max(errors)<.0005:break
        targets={}
        for geom,entries,_ in ik.legs:
            for _,qpos,aid,_ in entries:
                name=m.id2name(aid,'actuator').split('/')[-1]
                bias=loaded_targets[name]-ik.initial_angles[aid]
                target=float(d.qpos[qpos]+bias)
                if m.actuator_ctrllimited[aid]:target=float(np.clip(target,*m.actuator_ctrlrange[aid]))
                targets[aid]=target
        return targets,dict(virtual_model_pose_only=True,live_state_unchanged=True,
            maximum_kinematic_foot_error_cm=max(errors),iterations=iteration+1,
            desired_origin_cm=origin.tolist(),supported_claws=sorted(supported),
            limits='Kinematic target fit only; actual landing/hold/walking must qualify independently.')
    finally:
        trial.free()
        assert float(physics.data.time)==clock
        assert all(np.array_equal(getattr(physics.data,k),v) for k,v in state.items()),'IK modified live physical state'
