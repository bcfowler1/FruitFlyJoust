"""Separate staged-descent candidate; frozen baseline untouched.

Qualification probe: published walking/wing policies, one unreset full body.

Transfer to active legs and wings is experimental. No root wrench or pose handover.
"""
import json,time
from types import MethodType
from pathlib import Path
import mujoco
from evaluate_trained_flight import tf,np,ASSETS,flight_imitation
from flybody.fly_envs import walk_imitation
from flybody.tasks.constants import _WING_PARAMS
from optimized_flight_policy import CompiledMeanPolicy
from rider_load import RiderLoad
from flybody.quaternions import quat_dist_short_arc
from landing_contacts import claw_contacts

def observation(task,physics,flight=False):
    values={key:np.asarray(item(physics),dtype=np.float32)[None,...]
        for key,item in task.observables.items() if item.enabled}
    # The published flight network has no activation-state input. Leg/abdomen
    # filter states still evolve physically; they are not supplied to that policy.
    if flight:values['walker/actuator_activation']=np.empty((1,0),np.float32)
    return values

def bind_controls(env,physics):
    walker=env.task._walker;mapping=[]
    for key,selection in walker._action_indices.items():
        if not selection or not walker._ctrl_indices[key]:continue
        names=[env.physics.model.id2name(i,'actuator') for i in walker._ctrl_indices[key]]
        ids=[physics.model.name2id(name,'actuator') for name in names]
        mapping.append((np.asarray(ids),np.asarray(selection)))
    def apply(self,physics,action,random_state):
        self._prev_action[:]=action;physics.data.ctrl[:]=0
        for ids,selection in mapping:physics.data.ctrl[ids]=action[selection]
    walker.apply_action=MethodType(apply,walker)
    walker._physical_action_mapping=mapping

def joint_state(physics,walker):
    result={}
    for joint in walker.mjcf_model.find_all('joint'):
        if joint.tag!='joint':continue
        state=physics.bind(joint)
        result[joint.name]=dict(position=np.asarray(state.qpos).tolist(),velocity=np.asarray(state.qvel).tolist())
    return dict(joints=result,activation=physics.data.act.tolist(),controls=physics.data.ctrl.tolist())

def main(launch_height=.15,output_name='unified-policy-evaluation.json',landing=False,upright_landing=True,contact_handover=False,stance_before_launch=False,leg_ik=False,claw_hold=False,resume_walking=False,seed=42,grip_ramp=.2,stance_ramp=.5,walking_only=False,recover_stance=False,hold_seconds=.3,hold_head=False,filter_settle_seconds=0,claw_release_seconds=0,command_slew_rate=0,walking_seconds=.5,match_resume_height=False,contact_guard=False,guard_leg_ik=False,resume_speed=.2,grip_min_up=-1,balanced_grip=False,body_recovery=False,replant_feet=False,grip_max_speed=float("inf"),touchdown_recovery=False,adaptive_grip=False,automatic_recovery=False,heading_leg_ik=False,loaded_stance_handover=False,landing_pitch_degrees=None,capsule_clearance=False,synchronize_wings=False,complete_contacts=False,replant_all_feet=False,measured_stance_recovery=False,swing_posture_gain=0,restore_loaded_controls=False,walking_blend_seconds=.25):
    start=time.perf_counter();rng=np.random.RandomState(42)
    reference_frames=10000 if walking_seconds>2 or replant_feet else 4000
    qpos=np.zeros((reference_frames,7));qpos[:,0]=np.arange(reference_frames)*.002;qpos[:,2]=.14355;qpos[:,3]=1
    qvel=np.zeros((reference_frames,6));qvel[:,0]=1
    walk=walk_imitation(terminal_com_dist=float('inf'),random_state=np.random.RandomState(seed))
    walk.task._traj_generator.set_next_trajectory(qpos,qvel);walk.reset()
    flight=flight_imitation(wpg_pattern_path=str(ASSETS/'wing_pattern_fmech.npy'),terminal_com_dist=float('inf'),random_state=np.random.RandomState(seed))
    flight.reset()
    full=walk_imitation(disable_wings=False,terminal_com_dist=float('inf'),random_state=np.random.RandomState(seed))
    walker=full.task._walker
    for actuator in walker.mjcf_model.find_all('actuator'):
        if 'wing' in actuator.name:actuator.dyntype='none';actuator.dynprm=(0,)
    for i,axis in enumerate(('yaw','roll','pitch')):
        walker.mjcf_model.find('default',axis).general.gainprm[0]=_WING_PARAMS['gainprm'][i]
    for geom in walker.mjcf_model.find_all('geom'):
        if 'fluid' in geom.name:geom.fluidshape='ellipsoid';geom.fluidcoef=_WING_PARAMS['fluidcoef']
    wing=walker.mjcf_model.find('default','wing').joint
    wing.stiffness=_WING_PARAMS['stiffness'];wing.damping=_WING_PARAMS['damping']
    full.task._traj_generator.set_next_trajectory(qpos,qvel);full.reset()
    physics=full.physics;physics.model.opt.timestep=.00005
    load=RiderLoad(physics).apply(physics)
    bind_controls(walk,physics);bind_controls(flight,physics)
    policies={name:CompiledMeanPolicy(tf.saved_model.load(str(ASSETS/folder)))
        for name,folder in [('walk','walking'),('flight','flight')]}
    anchor=physics.model.name2id('walker/thorax','body')
    initial=physics.data.xpos[anchor].copy();rows=[]
    for step in range(200):
        action=policies['walk'](observation(walk.task,physics)).mean().numpy()[0]
        spec=walk.action_spec();walk.task.before_step(physics,np.clip(action,spec.minimum,spec.maximum),rng)
        physics.step(40)
        if not np.isfinite(physics.data.qpos).all():raise RuntimeError('Walking transfer became nonfinite')
    if walking_only:
        up_values=[]
        for step in range(1500):
            action=policies['walk'](observation(walk.task,physics)).mean().numpy()[0]
            spec=walk.action_spec();walk.task.before_step(physics,np.clip(action,spec.minimum,spec.maximum),rng)
            physics.step(40)
            up_values.append(float(physics.data.xmat[anchor].reshape(3,3)[2,2]))
        finite=bool(np.isfinite(physics.data.qpos).all() and np.isfinite(physics.data.qvel).all())
        displacement=float(np.linalg.norm((physics.data.xpos[anchor]-initial)[:2]))
        result=dict(qualified=bool(finite and min(up_values)>.85 and displacement>.01),
            walking_only=True,full_wing_and_leg_body=True,random_seed=seed,finite=finite,
            minimum_up_cosine=min(up_values),displacement_cm=displacement,
            simulation_seconds=float(physics.data.time),rider_load=load,
            pose_reset=False,root_external_force=False,wall_seconds=time.perf_counter()-start)
        Path(__file__).with_name(output_name).write_text(json.dumps(result,indent=2));print(json.dumps(result),flush=True)
        return
    if stance_before_launch:
        walk.task._ref_qpos[:,:2]=physics.data.qpos[:2]
        walk.task._ref_qvel[:]=0
        for _ in range(150):
            action=policies['walk'](observation(walk.task,physics)).mean().numpy()[0]
            spec=walk.action_spec();walk.task.before_step(physics,np.clip(action,spec.minimum,spec.maximum),rng)
            physics.step(40)
            if not np.isfinite(physics.data.qpos).all():raise RuntimeError('Prelaunch stance became nonfinite')
    walked=physics.data.xpos[anchor].copy();handover_position=physics.data.qpos.copy();handover_velocity=physics.data.qvel.copy()
    preflight_state=joint_state(physics,walker)
    loaded_stance_targets={}
    for joint in walker.mjcf_model.find_all('joint'):
        if not any(segment in joint.name for segment in ('T1','T2','T3')):continue
        actuator=walker.mjcf_model.find('actuator',joint.name)
        if actuator is None:continue
        aid=physics.model.name2id(actuator.full_identifier,'actuator');adr=int(physics.model.actuator_actadr[aid])
        loaded_stance_targets[joint.name]=float(physics.data.act[adr]) if adr>=0 else float(physics.data.ctrl[aid])
    preflight_state['applied_leg_targets']=loaded_stance_targets
    ik=None
    if leg_ik:
        from landing_leg_ik import LandingLegIK
        ik=LandingLegIK(physics,walker,anchor,capsule_clearance=capsule_clearance)
        ik.swing_posture_gain=swing_posture_gain
    # Translate the task's ghost reference to the launch location. Only ghost
    # reference state changes; physical body qpos/qvel and time remain continuous.
    reference_frames=40000 if landing else 15000
    ref=np.repeat(flight.task._ref_qpos[0:1],reference_frames,axis=0)
    ref[:,:3]=walked+np.array([0,0,.15]);flight.task._ref_qpos=ref
    flight.task._ref_qvel=np.zeros((reference_frames,6))
    flight.task._step_counter=int(round(physics.data.time/flight.control_timestep()))
    flight_actuators=[physics.model.name2id(item.full_identifier,'actuator') for item in flight.task._walker.actuators]
    # Restore the unfiltered actuator dynamics the flight policy was trained on.
    # Retain allocated walking filter states so no qpos/qvel/time reset is needed.
    walking_dynamics=physics.model.actuator_dyntype.copy()
    physics.model.actuator_dyntype[flight_actuators]=mujoco.mjtDyn.mjDYN_NONE
    pose_jump=float(np.max(np.abs(physics.data.qpos-handover_position)))
    velocity_jump=float(np.max(np.abs(physics.data.qvel-handover_velocity)))
    leg_joints=[joint for joint in walker.mjcf_model.find_all('joint') if any(part in joint.name for part in ('coxa','femur','tibia','tarsus'))]
    initial_leg_angles={joint.name:float(physics.bind(joint).qpos[0]) for joint in leg_joints}
    wing_controls=[physics.model.name2id(item.full_identifier,'actuator') for item in flight.task._walker.actuators if 'wing' in item.name]
    wing_phase_alignment=None
    if synchronize_wings:
        generator=flight.task._wbpg
        frequency=int(np.argmin(np.abs(generator.beat_freqs-generator.base_beat_freq)))
        sequence=generator.traj_ctrl[frequency]
        measured=physics.bind(flight.task._wing_joints).qpos.copy()
        # Select a command phase from the measured live joints. Do not assign
        # physical wing angles/velocities or change body time.
        candidates=np.where(sequence['phase']<1)[0]
        index=int(candidates[np.argmin(np.sum((sequence['traj'][candidates]-measured)**2,axis=1))])
        phase=float(sequence['phase'][index])
        before=physics.data.qpos.copy();velocity_before=physics.data.qvel.copy();time_before=float(physics.data.time)
        command=generator.reset(initial_phase=phase)
        assert np.array_equal(before,physics.data.qpos) and np.array_equal(velocity_before,physics.data.qvel) and time_before==physics.data.time
        wing_phase_alignment=dict(phase=phase,measured_angles=measured.tolist(),command_angles=command.tolist(),
            command_error_rad=float(np.linalg.norm(command-measured)),physical_state_unchanged=True)
    # Actual joint targets retract the legs; never assign the physical pose.
    flight_errors=[];ground_contacts=[];upright=[];attitude_errors=[];retraction=0.
    for step in range(5000):
        # First clear the ground at the tested takeoff reference, then move
        # the hover command smoothly. Avoid a new command discontinuity.
        height=.15+(launch_height-.15)*min(1.,max(0.,(step*.0002-.25)/.5))
        ref[:,:3]=walked+np.array([0,0,height])
        action=policies['flight'](observation(flight.task,physics,True)).mean().numpy()[0]
        spec=flight.action_spec();flight.task.before_step(physics,np.clip(action,spec.minimum,spec.maximum).copy(),rng)
        physics.data.ctrl[wing_controls]*=min(1.,step/150)
        retraction=max(retraction,min(1.,max(0.,(float(physics.data.xpos[anchor,2])-walked[2]-.05)/.1)))
        for joint in leg_joints:
            actuator=walker.mjcf_model.find('actuator',joint.name)
            if actuator is not None:
                spring=joint.springref or joint.dclass.joint.springref or 0
                physics.bind(actuator).ctrl=(1-retraction)*initial_leg_angles[joint.name]+retraction*spring
        physics.step(4)
        if not np.isfinite(physics.data.qpos).all():raise RuntimeError('Flight transfer became nonfinite')
        on_ground=sum(1 for c in physics.data.contact if 0 in (physics.model.geom_bodyid[c.geom1],physics.model.geom_bodyid[c.geom2]))
        error=float(np.linalg.norm(physics.data.xpos[anchor]-ref[0,:3]))
        up=float(physics.data.xmat[anchor].reshape(3,3)[2,2])
        attitude=float(quat_dist_short_arc(physics.data.qpos[3:7],ref[0,3:7]))
        if step>=3750:flight_errors.append(error);ground_contacts.append(on_ground);upright.append(up);attitude_errors.append(attitude)
        if step%250==0:rows.append(dict(time=float(physics.data.time),position_cm=physics.data.xpos[anchor].tolist(),ground_contacts=on_ground,reference_error_cm=error,up_cosine=up))
    final=physics.data.xpos[anchor].copy()
    rms=float(np.sqrt(np.mean(np.square(flight_errors))))
    result=dict(wing_phase_alignment=wing_phase_alignment,qualified=bool(rms<.15 and max(ground_contacts)==0 and max(attitude_errors)<.15),random_seed=seed,
        qualification_limits=dict(final_quarter_second_rms_cm=.15,maximum_ground_contacts=0,maximum_reference_attitude_error_rad=.15),
        reference_quaternion=ref[0,3:7].tolist(),maximum_reference_attitude_error_rad=max(attitude_errors),
        final_quarter_second_rms_cm=rms,maximum_ground_contacts=max(ground_contacts),minimum_up_cosine=min(upright),
        same_physics_instance=True,pose_handover_jump=pose_jump,velocity_handover_jump=velocity_jump,
        root_external_force=False,walking_displacement_cm=(walked-initial).tolist(),flight_displacement_cm=(final-walked).tolist(),
        launch_height_cm=.15,hover_height_cm=launch_height,rider_load=load,rows=rows,wall_seconds=time.perf_counter()-start,
        limitations='Experimental policy transfer to active legs/wings; upstream flight actuator filtering, 30 ms wing-force ramp and monotonic clearance-dependent leg targets. Launch reference is 1.5 mm; hover commands change smoothly after 250 ms. Attitude is measured against the published 47.5 degree reference pitch, not vertical. Synthetic ghost command; no connectome, neural landing, or biological qualification.')
    if landing:
        print(json.dumps(dict(stage='landing',seed=seed,hover_qualified=result['qualified'],simulation_time=float(physics.data.time))),flush=True)
        # New qualification, separate from hover: extend actual legs and descend
        # using the wing policy. Never assign qpos/qvel or apply root forces.
        touchdown=None;stable_time=0.;landing_rows=[];start_time=float(physics.data.time)
        flight_quaternion=ref[0,3:7].copy()
        walk_started=None;walk_counter=0;wing_at_contact=None;walking_ctrl=None
        hold_started=None;grip_start_state=None;contact_completion_steps=0;free_contact_targets={}
        reached_claws=set();reached_targets={};first_reach_time=None;landing_native_controls=None;landing_native_updates=0;landing_native_initialized=False
        claw_actuators=[i for i in range(physics.model.nu) if 'adhere_claw' in (physics.model.id2name(i,'actuator') or '')]
        for step in range(20000):
            # Descend above the established landing envelope with legs tucked.
            # Begin floor-reaching IK only after reaching the .15 cm command.
            approach_seconds=max(0.,(launch_height-.15)/.125)
            approach_progress=min(1.,step*.0002/max(approach_seconds,1e-9))
            progress=min(1.,max(0.,step*.0002-approach_seconds)/1.2)
            # Ask for upright touchdown through the ghost reference only.
            # The physical quaternion remains untouched; policy extrapolation
            # to this new attitude must qualify rather than being assumed safe.
            upright_blend=min(1.,step*.0002/.6) if upright_landing or landing_pitch_degrees is not None else 0.
            target_pitch=0. if landing_pitch_degrees is None else np.deg2rad(landing_pitch_degrees)
            landing_quaternion=np.array([np.cos(target_pitch/2),0,-np.sin(target_pitch/2),0])
            target_quaternion=(1-upright_blend)*flight_quaternion+upright_blend*landing_quaternion
            ref[:,3:7]=target_quaternion/np.linalg.norm(target_quaternion)
            ref[:,:3]=walked+np.array([0,0,(.15*(1-progress)+max(0.,launch_height-.15)*(1-approach_progress))])
            action=policies['flight'](observation(flight.task,physics,True)).mean().numpy()[0]
            spec=flight.action_spec();flight.task.before_step(physics,np.clip(action,spec.minimum,spec.maximum).copy(),rng)
            for joint in leg_joints:
                actuator=walker.mjcf_model.find('actuator',joint.name)
                if actuator is not None:
                    spring=joint.springref or joint.dclass.joint.springref or 0
                    physics.bind(actuator).ctrl=(1-progress)*spring+progress*initial_leg_angles[joint.name]
            feet,body_contacts=claw_contacts(physics.model,physics.data);contacts=len(feet)
            # Grip each physically contacting claw before the three-claw
            # support transfer. Adhesion applies only through actual contacts.
            if claw_hold and contacts and body_contacts==0 and float(physics.data.xmat[anchor].reshape(3,3)[2,2])>.45 and np.linalg.norm(physics.data.qvel[:3])<=grip_max_speed:
                if first_reach_time is None:first_reach_time=float(physics.data.time)
                for foot in feet:
                    if foot in reached_claws:continue
                    reached_claws.add(foot)
                    if ik:
                        for geom,entries,_ in ik.legs:
                            if physics.model.id2name(geom,'geom')==foot:
                                for _,qpos,aid,_ in entries:reached_targets[aid]=float(ik.targets.get(aid,physics.data.ctrl[aid]))
            for aid in claw_actuators:
                suffix=physics.model.id2name(aid,'actuator').split('adhere_claw_')[-1]
                if any(foot.endswith(suffix+'_collision') for foot in reached_claws):physics.data.ctrl[aid]=1
            if ik and step*.0002>=approach_seconds:
                if step%10==0:
                    if heading_leg_ik:
                        rotation=physics.data.xmat[anchor].reshape(3,3)
                        yaw_ik=float(np.arctan2(rotation[1,0],rotation[0,0]));c,s=np.cos(yaw_ik),np.sin(yaw_ik)
                        ik.recovery_rotation=np.array([[c,-s,0],[s,c,0],[0,0,1]])
                    ik.update()
                ik.apply()
                if hold_started is None and reached_claws:
                    # Reach free feet through bounded joint IK while preserving
                    # contact-support commands and their physical servo load.
                    ik.lower_unloaded_feet(feet)
                    for aid,target in reached_targets.items():physics.data.ctrl[aid]=target
            support_balanced=any('T1_' in foot for foot in feet) and any('T3_' in foot for foot in feet)
            up_for_grip=float(physics.data.xmat[anchor].reshape(3,3)[2,2])
            rear_pair=all(any('T3_'+side in foot for foot in feet) for side in ('left','right'))
            grip_attitude_ok=((rear_pair and up_for_grip>=.45) or up_for_grip>=.65) if adaptive_grip else up_for_grip>=grip_min_up
            if claw_hold and (not balanced_grip or support_balanced) and hold_started is None and contacts>=3 and body_contacts==0 and grip_attitude_ok and np.linalg.norm(physics.data.qvel[:3])<=grip_max_speed:
                grip_start_state=dict(up_cosine=float(physics.data.xmat[anchor].reshape(3,3)[2,2]),claws=sorted(feet),root=physics.data.qpos[:7].tolist(),speed_cm_s=float(np.linalg.norm(physics.data.qvel[:3])))
                grip_start_state['root_velocity']=physics.data.qvel[:6].tolist()
                grip_start_state['wing_velocity']=[float(physics.data.qvel[physics.model.jnt_dofadr[physics.model.actuator_trnid[i,0]]]) for i in wing_controls]
                print(json.dumps(dict(stage='claw_grip_started',state=grip_start_state)),flush=True)
                hold_started=float(physics.data.time)
                if touchdown_recovery:
                    ik.begin_body_recovery(float(walk.task._ref_qpos[0,2]),gain=.5,maximum_step=.005)
                    yaw_contact=float(np.arctan2(physics.data.xmat[anchor].reshape(3,3)[1,0],physics.data.xmat[anchor].reshape(3,3)[0,0]))
                    c,s=np.cos(yaw_contact),np.sin(yaw_contact)
                    desired_rotation=np.array([[c,-s,0],[s,c,0],[0,0,1]])
                    origin=physics.data.xpos[anchor].copy();origin[2]=float(walk.task._ref_qpos[0,2])
                    for geom,_,_ in ik.legs:
                        foot=physics.data.geom_xpos[geom].copy();foot[2]=float(physics.model.geom_size[geom,0])+.001
                        ik.body_recovery[geom]=desired_rotation.T@(foot-origin)
            if hold_started is not None:
                elapsed_hold=float(physics.data.time)-hold_started
                flight_torque=physics.data.ctrl[wing_controls].copy()
                if not landing_native_initialized:
                    physics.model.actuator_dyntype[:]=walking_dynamics
                    walk.task._ref_qpos[:,:2]=physics.data.qpos[:2]
                    matrix=physics.data.xmat[anchor].reshape(3,3)
                    yaw=float(np.arctan2(matrix[1,0],matrix[0,0]))
                    heading_quat=np.array([np.cos(yaw/2),0,0,np.sin(yaw/2)])
                    if np.dot(heading_quat,physics.data.qpos[3:7])<0:heading_quat=-heading_quat
                    walk.task._ref_qpos[:,3:7]=heading_quat
                    walk.task._ref_qvel[:]=0
                    landing_native_initialized=True
                if landing_native_controls is None or step%10==0:
                    walk.task._step_counter=landing_native_updates
                    native_action=policies['walk'](observation(walk.task,physics)).mean().numpy()[0]
                    native_spec=walk.action_spec()
                    walk.task.before_step(physics,np.clip(native_action,native_spec.minimum,native_spec.maximum),rng)
                    landing_native_controls=physics.data.ctrl.copy()
                    landing_native_updates+=1
                physics.data.ctrl[:]=landing_native_controls
                physics.data.ctrl[wing_controls]=flight_torque*max(0.,1-elapsed_hold/grip_ramp)
                physics.data.ctrl[claw_actuators]=1
                stance_blend=min(1.,elapsed_hold/stance_ramp)

            if complete_contacts and hold_started is not None and stance_blend>=1 and contacts>=3 and body_contacts==0 and up_for_grip>.9 and np.linalg.norm(physics.data.qvel[:3])<.5:
                if step%10==0:ik.lower_unloaded_feet(feet)
                else:
                    # Hold the last free-foot targets between 2ms IK updates.
                    for geom,entries,_ in ik.legs:
                        if physics.model.id2name(geom,'geom') not in feet:
                            for _,_,aid,_ in entries:physics.data.ctrl[aid]=free_contact_targets.get(aid,physics.data.ctrl[aid])
                free_contact_targets={aid:float(physics.data.ctrl[aid]) for geom,entries,_ in ik.legs if physics.model.id2name(geom,'geom') not in feet for _,_,aid,_ in entries}
                contact_completion_steps+=1
            if contact_handover and walk_started is None and contacts>=1 and body_contacts==0:
                walk_started=float(physics.data.time);wing_at_contact=physics.data.ctrl[wing_controls].copy()
                # Translate only the walking ghost to the contact location.
                walk.task._ref_qpos[:,:2]=physics.data.qpos[:2]
                walk.task._ref_qvel[:]=0
                walk.task._step_counter=0
            if walk_started is not None:
                if step%10==0:
                    walking_action=policies['walk'](observation(walk.task,physics)).mean().numpy()[0]
                    walking_spec=walk.action_spec()
                    walk.task.before_step(physics,np.clip(walking_action,walking_spec.minimum,walking_spec.maximum),rng)
                    walking_ctrl=physics.data.ctrl.copy()
                    walk_counter+=1
                if walking_ctrl is not None:physics.data.ctrl[:]=walking_ctrl
                physics.data.ctrl[wing_controls]=wing_at_contact*max(0.,1-(float(physics.data.time)-walk_started)/.05)
            if contacts>=4 and float(physics.data.xmat[anchor].reshape(3,3)[2,2])>.9:
                if touchdown is None:touchdown=float(physics.data.time)
                physics.data.ctrl[wing_controls]*=max(0.,1-(float(physics.data.time)-touchdown)/.05)
            physics.step(4)
            feet,body_contacts=claw_contacts(physics.model,physics.data);contacts=len(feet)
            finite=bool(np.isfinite(physics.data.qpos).all() and np.isfinite(physics.data.qvel).all())
            up=float(physics.data.xmat[anchor].reshape(3,3)[2,2]);speed=float(np.linalg.norm(physics.data.qvel[:3]))
            stable_time=stable_time+.0002 if contacts>=4 and body_contacts==0 and up>.9 and speed<.5 else 0.
            if step%500==0:landing_rows.append(dict(time=float(physics.data.time),contacts=contacts,body_contacts=body_contacts,up_cosine=up,speed_cm_s=speed))
            handover_ready=bool(not automatic_recovery or hold_started is None or float(physics.data.time)-hold_started>=(max(grip_ramp,stance_ramp) if loaded_stance_handover else grip_ramp+stance_ramp))
            if not finite or (stable_time>=.1 and handover_ready):break
        result['landing']=dict(proprioceptive_trained_walking_policy_after_grip=True,landing_native_policy_updates=landing_native_updates,individual_contact_grip=True,first_reach_time=first_reach_time,reached_claws=sorted(reached_claws),approach_seconds=approach_seconds,approach_legs_tucked=True,qualified=bool(finite and stable_time>=.1 and handover_ready),contact_transition_completed=handover_ready,stable_seconds=stable_time,landing_pitch_reference_degrees=landing_pitch_degrees,upright_reference=upright_landing,stance_before_launch=stance_before_launch,
            loaded_stance_handover=loaded_stance_handover,loaded_stance_targets=loaded_stance_targets if loaded_stance_handover else None,heading_relative_leg_ik=heading_leg_ik,contact_handover=contact_handover,walking_policy_handover_time=walk_started,physical_leg_ik=leg_ik,capsule_floor_clearance=capsule_clearance,unsupported_contact_completion=complete_contacts,contact_completion_steps=contact_completion_steps,
            contact_only_claw_hold=claw_hold,claw_hold_started=hold_started,
            diagnostic_adaptive_grip=adaptive_grip,diagnostic_touchdown_recovery=touchdown_recovery,touchdown_recovery_controller=dict(gain=.5,maximum_command_step_rad=.005,control_seconds=.002) if touchdown_recovery else None,diagnostic_balanced_grip=balanced_grip,diagnostic_grip_min_up=grip_min_up,diagnostic_grip_max_speed_cm_s=grip_max_speed if np.isfinite(grip_max_speed) else None,grip_start_state=grip_start_state,
            wing_cut_seconds=grip_ramp,stance_blend_seconds=stance_ramp,
            touchdown_time=touchdown,final_up_cosine=up,final_speed_cm_s=speed,final_contacts=contacts,final_body_contacts=body_contacts,
            elapsed_simulation_seconds=float(physics.data.time)-start_time,rows=landing_rows,
            limits=dict(minimum_contacts=4,minimum_up_cosine=.9,maximum_speed_cm_s=.5,minimum_stable_seconds=.1),
            limitations='Engineering floor descent and leg extension; distinct claw contacts required and non-claw collisions excluded from stable hold. No walking resume or biological landing validation.')
        result['qualified']=bool(result['qualified'] and result['landing']['qualified'])
        if resume_walking and result['landing']['qualified']:
            print(json.dumps(dict(stage='sustained_hold',seed=seed,simulation_time=float(physics.data.time))),flush=True)
            held_contacts=[];held_up=[];held_speed=[]
            landing_applied_targets={}
            for joint in leg_joints:
                actuator=walker.mjcf_model.find('actuator',joint.name)
                if actuator is not None:
                    aid=physics.model.name2id(actuator.full_identifier,'actuator');adr=int(physics.model.actuator_actadr[aid])
                    landing_applied_targets[joint.name]=float(physics.data.act[adr]) if adr>=0 else float(physics.data.ctrl[aid])
            for _ in range(max(1,round(hold_seconds/.002))):
                physics.data.ctrl[wing_controls]=0;physics.data.ctrl[claw_actuators]=1
                for joint in leg_joints:
                    actuator=walker.mjcf_model.find('actuator',joint.name)
                    if actuator is not None:physics.bind(actuator).ctrl=landing_applied_targets[joint.name]
                physics.step(40)
                feet,other=claw_contacts(physics.model,physics.data)
                held_contacts.append(len(feet) if other==0 else 0)
                held_up.append(float(physics.data.xmat[anchor].reshape(3,3)[2,2]))
                held_speed.append(float(np.linalg.norm(physics.data.qvel[:3])))
            hold_ok=min(held_contacts)>=4 and min(held_up)>.9 and max(held_speed)<.5
            print(json.dumps(dict(stage='walking_resume',seed=seed,hold_qualified=bool(hold_ok),simulation_time=float(physics.data.time))),flush=True)
            if ik is not None and recover_stance:
                matrix=physics.data.xmat[anchor].reshape(3,3)
                recovery_yaw=float(np.arctan2(matrix[1,0],matrix[0,0]))
                c,s=np.cos(recovery_yaw),np.sin(recovery_yaw)
                ik.recovery_rotation=np.array([[c,-s,0],[s,c,0],[0,0,1]])
                for _ in range(250):
                    ik.update();ik.apply()
                    physics.data.ctrl[wing_controls]=0;physics.data.ctrl[claw_actuators]=1
                    physics.step(40)
                print(json.dumps(dict(stage='stance_recovery',up_cosine=float(physics.data.xmat[anchor].reshape(3,3)[2,2]))),flush=True)
            native_prediction=None;requested_body_recovery=body_recovery
            if automatic_recovery:
                from handoff_prediction import predict_native_handoff
                native_prediction=predict_native_handoff(walk,physics,policies['walk'],observation,rng,anchor,
                    wing_controls,claw_actuators,walking_dynamics,resume_speed,filter_settle_seconds,claw_release_seconds,walking_blend_seconds)
                native_prediction['real_physical_state_unchanged']=True
                body_recovery=bool(body_recovery and not native_prediction['qualified'])
                replant_feet=bool(replant_feet and body_recovery)
                print(json.dumps(dict(stage='native_handoff_prediction',result=native_prediction)),flush=True)
            recovery_rows=[];recovery_safe=[]
            if body_recovery:
                ik.begin_body_recovery(ik.initial_height if measured_stance_recovery else float(walk.task._ref_qpos[0,2]))
                for recovery_index in range(500):
                    ik.update();ik.apply();physics.data.ctrl[wing_controls]=0;physics.data.ctrl[claw_actuators]=1
                    physics.step(40)
                    feet,other=claw_contacts(physics.model,physics.data)
                    recovery_safe.append(len(feet)>=4 and other==0 and float(physics.data.xmat[anchor].reshape(3,3)[2,2])>.9)
                    if recovery_index%25==0:
                        recovery_rows.append(dict(seconds=(recovery_index+1)*.002,up=float(physics.data.xmat[anchor].reshape(3,3)[2,2]),claws=len(feet),other_contacts=other,height=float(physics.data.xpos[anchor,2])))
                    if not recovery_safe[-1]:break
                hold_ok=bool(hold_ok and all(recovery_safe))
                print(json.dumps(dict(stage='body_posture_recovery',rows=recovery_rows)),flush=True)
            replant_rows=[]
            if replant_feet and hold_ok:
                # Engineering single-foot repositioning: actual joint actuators,
                # five retained claw contacts, no root state/force assignment.
                matrix=physics.data.xmat[anchor].reshape(3,3)
                yaw_replant=float(np.arctan2(matrix[1,0],matrix[0,0]))
                c,s=np.cos(yaw_replant),np.sin(yaw_replant)
                wanted_rotation=np.array([[c,-s,0],[s,c,0],[0,0,1]])
                for geom,entries,offset in ik.legs:
                    suffix=physics.model.id2name(geom,'geom').split('tarsal_claw_')[1].replace('_collision','')
                    if not replant_all_feet and not suffix.startswith('T1_'):continue
                    adhesion=next(a for a in claw_actuators if physics.model.id2name(a,'actuator').endswith(suffix))
                    start_foot=physics.data.geom_xpos[geom].copy()
                    target_foot=physics.data.xpos[anchor]+wanted_rotation@ik.initial_rotation.T@offset
                    target_foot[2]=ik.initial_height+offset[2] if measured_stance_recovery else float(physics.model.geom_size[geom,0])+.001
                    foot_safe=[]
                    for foot_step in range(350):
                        phase=min(1.,foot_step*.002/.5)
                        smooth=phase*phase*(3-2*phase)
                        goal=start_foot*(1-smooth)+target_foot*smooth
                        goal[2]+=.02*np.sin(np.pi*phase)
                        ik.swing_goal=(geom,goal)
                        ik.update();ik.apply()
                        physics.data.ctrl[wing_controls]=0;physics.data.ctrl[claw_actuators]=1
                        if phase<1:physics.data.ctrl[adhesion]=0
                        physics.step(40)
                        feet,other=claw_contacts(physics.model,physics.data)
                        up=float(physics.data.xmat[anchor].reshape(3,3)[2,2])
                        foot_safe.append(len(feet)>=4 and other==0 and up>.9)
                        if not foot_safe[-1]:break
                    replant_rows.append(dict(foot=suffix,completed_seconds=(foot_step+1)*.002,
                        claws=len(feet),other_contacts=other,up=up,
                        start_foot_cm=start_foot.tolist(),target_foot_cm=target_foot.tolist(),
                        actual_foot_cm=physics.data.geom_xpos[geom].tolist(),
                        joint_state={physics.model.id2name(joint,'joint'):dict(position=float(physics.data.qpos[qpos]),
                            control=float(physics.data.ctrl[actuator]),limits=physics.model.jnt_range[joint].tolist())
                            for _,qpos,actuator,joint in entries},
                        target_error_cm=float(np.linalg.norm(physics.data.geom_xpos[geom]-target_foot))))
                    hold_ok=bool(hold_ok and all(foot_safe) and geom in {physics.model.name2id(name,'geom') for name in feet})
                    ik.swing_goal=None
                    ik.begin_body_recovery(ik.initial_height if measured_stance_recovery else float(walk.task._ref_qpos[0,2]))
                    if not hold_ok:break
                print(json.dumps(dict(stage='foot_replant_recovery',rows=replant_rows,qualified=hold_ok)),flush=True)
            resume_position=physics.data.qpos.copy();resume_velocity=physics.data.qvel.copy();resume_time=float(physics.data.time)
            filter_settle_rows=[]
            if filter_settle_seconds:
                physics.model.actuator_dyntype[:]=walking_dynamics
                for settle_index in range(round(filter_settle_seconds/.002)):
                    physics.step(40)
                    feet,other=claw_contacts(physics.model,physics.data)
                    filter_settle_rows.append(dict(seconds=(settle_index+1)*.002,claws=len(feet),other_contacts=other,
                        up=float(physics.data.xmat[anchor].reshape(3,3)[2,2]),speed_cm_s=float(np.linalg.norm(physics.data.qvel[:3]))))
                hold_ok=bool(hold_ok and all(r['claws']>=4 and r['other_contacts']==0 and r['up']>.9 and r['speed_cm_s']<.5 for r in filter_settle_rows))
                resume_position=physics.data.qpos.copy();resume_velocity=physics.data.qvel.copy();resume_time=float(physics.data.time)
            loaded_restore_rows=[]
            if restore_loaded_controls and hold_ok:
                physics.model.actuator_dyntype[:]=walking_dynamics
                restore_start=physics.data.ctrl.copy()
                restore_ids={physics.model.name2id(walker.mjcf_model.find('actuator',name).full_identifier,'actuator'):value for name,value in loaded_stance_targets.items()}
                for restore_index in range(750):
                    blend=min(1.,restore_index*.002/.5)
                    for aid,value in restore_ids.items():physics.data.ctrl[aid]=(1-blend)*restore_start[aid]+blend*value
                    physics.data.ctrl[wing_controls]=0
                    physics.data.ctrl[claw_actuators]=1
                    physics.step(40)
                    feet,other=claw_contacts(physics.model,physics.data)
                    up_restore=float(physics.data.xmat[anchor].reshape(3,3)[2,2]);speed_restore=float(np.linalg.norm(physics.data.qvel[:3]))
                    safe=len(feet)>=4 and other==0 and up_restore>.9 and speed_restore<.5
                    if restore_index%25==0 or not safe:loaded_restore_rows.append(dict(seconds=(restore_index+1)*.002,claws=len(feet),other_contacts=other,up=up_restore,speed=speed_restore))
                    hold_ok=bool(hold_ok and safe)
                    if not safe:break
                resume_position=physics.data.qpos.copy();resume_velocity=physics.data.qvel.copy();resume_time=float(physics.data.time)
                print(json.dumps(dict(stage='loaded_control_restore',qualified=hold_ok,rows=loaded_restore_rows)),flush=True)
            landed_state=joint_state(physics,walker)
            nominal_reference_height=float(walk.task._ref_qpos[0,2])
            if match_resume_height:walk.task._ref_qpos[:,2]=physics.data.qpos[2]
            walk.task._ref_qpos[:,:2]=physics.data.qpos[:2]
            resume_step=int(round(resume_time/walk.control_timestep()))
            heading=physics.data.xmat[anchor].reshape(3,3)[:2,0].copy()
            heading/=np.linalg.norm(heading)
            yaw=float(np.arctan2(heading[1],heading[0]))
            reference_time=np.maximum(0.,(np.arange(len(walk.task._ref_qpos))-resume_step)*.002)
            reference_distance=resume_speed*np.where(reference_time<.25,reference_time**2/.5,reference_time-.125)
            walk.task._ref_qpos[:,:2]+=reference_distance[:,None]*heading
            heading_quat=np.array([np.cos(yaw/2),0,0,np.sin(yaw/2)])
            if np.dot(heading_quat,physics.data.qpos[3:7])<0:heading_quat=-heading_quat
            walk.task._ref_qpos[:,3:7]=heading_quat
            walk.task._ref_qvel[:]=0;walk.task._ref_qvel[:,:2]=(resume_speed*np.minimum(1.,reference_time/.25))[:,None]*heading
            walk.task._step_counter=resume_step
            physics.model.actuator_dyntype[:]=walking_dynamics
            resume_jump=float(np.max(np.abs(physics.data.qpos-resume_position)))
            resume_velocity_jump=float(np.max(np.abs(physics.data.qvel-resume_velocity)))
            resumed_origin=physics.data.xpos[anchor].copy();resumed_up=[];resume_rows=[]
            stance_controls=physics.data.ctrl.copy()
            command_trace=[];contact_events=[]
            previous_feet,_=claw_contacts(physics.model,physics.data)
            actuator_names=[physics.model.id2name(i,'actuator') for i in range(physics.model.nu)]
            head_controls=[i for i,name in enumerate(actuator_names) if name and (name.endswith('/head') or '/head_' in name)]
            for ids,selection in walk.task._walker._physical_action_mapping:
                walk.task._walker._prev_action[selection]=stance_controls[ids]
            if guard_leg_ik:
                ik.posture_gain=.05
                c,s=np.cos(yaw),np.sin(yaw)
                ik.recovery_rotation=np.array([[c,-s,0],[s,c,0],[0,0,1]])
            guard_best_up=float(physics.data.xmat[anchor].reshape(3,3)[2,2])
            guard_rest_steps=0;tail_origin=None
            guard_blend=0.;guard_steps=0;guard_events=[];guard_was_active=False
            previous_controls=stance_controls.copy()
            for resume_index in range(round(walking_seconds/.002)):
                if resume_index==round((walking_seconds-.25)/.002):tail_origin=physics.data.xpos[anchor].copy()
                action=policies['walk'](observation(walk.task,physics)).mean().numpy()[0]
                spec=walk.action_spec();walk.task.before_step(physics,np.clip(action,spec.minimum,spec.maximum),rng)
                native_controls=physics.data.ctrl.copy()
                native_claw_controls=physics.data.ctrl[claw_actuators].copy()
                blend=min(1.,resume_index*.002/walking_blend_seconds) if walking_blend_seconds else 1.
                if contact_guard:
                    guard_feet,guard_other=claw_contacts(physics.model,physics.data)
                    guard_up=float(physics.data.xmat[anchor].reshape(3,3)[2,2])
                    guard_best_up=max(guard_best_up,guard_up)
                    guard_active=len(guard_feet)<3 or guard_up<max(.9,guard_best_up-.02) or guard_other>0
                    if guard_active and guard_blend==0 and len(guard_feet)>=3 and guard_up>.9 and guard_other==0 and np.linalg.norm(physics.data.qvel[:3])<.05:
                        guard_rest_steps+=1
                        if guard_rest_steps>=100:guard_best_up=guard_up;guard_active=False;guard_rest_steps=0
                    else:guard_rest_steps=0
                    guard_blend=float(np.clip(guard_blend+(-.002/.02 if guard_active else .002/.25),0,1))
                    blend=guard_blend
                    guard_steps+=int(guard_active)
                    if guard_active!=guard_was_active:guard_events.append(dict(seconds=resume_index*.002,active=guard_active,claws=len(guard_feet),up=guard_up))
                    guard_was_active=guard_active
                    if guard_leg_ik and guard_active:
                        ik.update()
                        for actuator,target in ik.targets.items():stance_controls[actuator]=target
                physics.data.ctrl[:]=stance_controls*(1-blend)+physics.data.ctrl*blend
                physics.data.ctrl[claw_actuators]=native_claw_controls
                if claw_release_seconds:
                    retained_grip=max(0.,1-resume_index*.002/claw_release_seconds)
                    physics.data.ctrl[claw_actuators]=np.maximum(native_claw_controls,retained_grip)
                if contact_guard:physics.data.ctrl[claw_actuators]=np.maximum(physics.data.ctrl[claw_actuators],1-blend)
                if command_slew_rate:
                    # Engineering diagnostic: bound actuator command changes, preserving native claw scheduling.
                    non_claw=np.ones(physics.model.nu,dtype=bool);non_claw[claw_actuators]=False
                    step_limit=command_slew_rate*.002
                    physics.data.ctrl[non_claw]=previous_controls[non_claw]+np.clip(physics.data.ctrl[non_claw]-previous_controls[non_claw],-step_limit,step_limit)
                if hold_head:physics.data.ctrl[head_controls]=stance_controls[head_controls]
                previous_controls=physics.data.ctrl.copy()
                physics.data.ctrl[wing_controls]=0;physics.step(40)
                for ids,selection in walk.task._walker._physical_action_mapping:
                    walk.task._walker._prev_action[selection]=physics.data.ctrl[ids]
                resumed_up.append(float(physics.data.xmat[anchor].reshape(3,3)[2,2]))
                feet,other=claw_contacts(physics.model,physics.data)
                if feet!=previous_feet:
                    contact_events.append(dict(seconds=(resume_index+1)*.002,
                        lost=sorted(previous_feet-feet),gained=sorted(feet-previous_feet),
                        up=resumed_up[-1],other_contacts=other))
                    previous_feet=feet.copy()
                if resume_index<100:
                    command_trace.append(dict(seconds=(resume_index+1)*.002,
                        feet=sorted(feet),other_contacts=other,up=resumed_up[-1],
                        native_controls=native_controls.tolist(),
                        applied_state=joint_state(physics,walker)))
                if resume_index%25==0:
                    feet,other=claw_contacts(physics.model,physics.data)
                    resume_rows.append(dict(seconds=resume_index*.002,up=resumed_up[-1],claws=len(feet),other_contacts=other,root=physics.data.qpos[:7].tolist(),speed_cm_s=float(np.linalg.norm(physics.data.qvel[:3]))))
                if resumed_up[-1]<=.85 or not np.isfinite(physics.data.qpos).all():break
            displacement=float(np.linalg.norm((physics.data.xpos[anchor]-resumed_origin)[:2]))
            resume_ok=bool(np.isfinite(physics.data.qpos).all() and min(resumed_up)>.85 and displacement>.01)
            legacy_resume_ok=resume_ok
            forward_displacement=float(np.dot((physics.data.xpos[anchor]-resumed_origin)[:2],heading))
            tail_forward=0. if tail_origin is None else float(np.dot((physics.data.xpos[anchor]-tail_origin)[:2],heading))
            resume_ok=bool(resume_ok and forward_displacement>.01 and tail_forward>.005)
            result['walking_resume']=dict(native_handoff_prediction=native_prediction,recovery_requested=requested_body_recovery,diagnostic_foot_replant=replant_feet,all_foot_replant=replant_all_feet,measured_stance_recovery=measured_stance_recovery,swing_posture_gain=swing_posture_gain,restored_loaded_controls=restore_loaded_controls,loaded_restore_grip=1 if restore_loaded_controls else None,loaded_restore_rows=loaded_restore_rows,foot_replant_rows=replant_rows,
                recovery_controller=dict(integral_gain=.1,maximum_command_step_rad=.001,control_seconds=.002,
                    posture_gain=ik.posture_gain,foot_lift_cm=.02 if replant_feet else 0,
                    foot_swing_seconds=.5,foot_settle_seconds=.2) if body_recovery else None,
                diagnostic_body_posture_recovery=body_recovery,body_recovery_target_height_cm=getattr(ik,'recovery_target_height',None),body_recovery_rows=recovery_rows,completed_recovery_seconds=(recovery_index+1)*.002 if body_recovery else 0,body_recovery_support_bias=getattr(ik,'support_bias',None),legacy_stability_displacement_qualified=bool(hold_ok and legacy_resume_ok),
                final_quarter_second_forward_cm=tail_forward,walking_limits=dict(minimum_up_cosine=.85,minimum_displacement_cm=.01,minimum_forward_displacement_cm=.01,minimum_final_quarter_second_forward_cm=.005),
                qualified=bool(hold_ok and resume_ok),hold_qualified=bool(hold_ok),
                preflight_state=preflight_state,landed_state=landed_state,
                actuator_names=actuator_names,command_trace=command_trace,contact_events=contact_events,
                diagnostic_guard_leg_ik=guard_leg_ik,diagnostic_contact_guard=contact_guard,guard_steps=guard_steps,guard_events=guard_events,forward_displacement_cm=float(np.dot((physics.data.xpos[anchor]-resumed_origin)[:2],heading)),
                diagnostic_match_resume_height=match_resume_height,nominal_reference_height_cm=nominal_reference_height,reference_height_cm=float(walk.task._ref_qpos[0,2]),
                diagnostic_head_command_hold=hold_head,head_hold_actuators=[actuator_names[i] for i in head_controls] if hold_head else [],diagnostic_command_slew_rate=command_slew_rate,
                physical_filter_settle_seconds=filter_settle_seconds,filter_settle_rows=filter_settle_rows,
                minimum_held_claws=min(held_contacts),minimum_held_up_cosine=min(held_up),maximum_held_speed_cm_s=max(held_speed),
                displacement_cm=displacement,minimum_walking_up_cosine=min(resumed_up),hold_seconds=hold_seconds,walking_seconds=walking_seconds,completed_walking_seconds=(resume_index+1)*.002,
                pose_jump=resume_jump,velocity_jump=resume_velocity_jump,time_reset=False,
                command_blend_seconds=walking_blend_seconds,claw_release_seconds=claw_release_seconds,claw_release='engineering grip ramp then native policy' if claw_release_seconds else 'native walking policy',
                initial_root=resume_position[:7].tolist(),reference_heading_rad=yaw,reference_heading_quaternion=heading_quat.tolist(),reference_quaternion_same_hemisphere=bool(np.dot(heading_quat,physics.data.qpos[3:7])>=0),rows=resume_rows,
                previous_action_matches_applied_controls=True,
                walking_command_cm_s=resume_speed,command_acceleration_seconds=.25,
                leg_actuator_stance_recovery_seconds=.5 if ik is not None and recover_stance else 0.,
                continuous_elapsed_seconds=float(physics.data.time)-resume_time)
            result['qualified']=bool(result['qualified'] and hold_ok and resume_ok)
    result['wall_seconds']=time.perf_counter()-start
    Path(__file__).with_name(output_name).write_text(json.dumps(result,indent=2))
    summary={k:v for k,v in result.items() if k!='rows'}
    if 'walking_resume' in summary:
        summary['walking_resume']={k:v for k,v in summary['walking_resume'].items()
            if k not in ('preflight_state','landed_state','actuator_names','command_trace','contact_events','rows','filter_settle_rows','body_recovery_rows','foot_replant_rows','body_recovery_support_bias','guard_events')}
    if 'landing' in summary:summary['landing']={k:v for k,v in summary['landing'].items() if k!='rows'}
    print(json.dumps(summary),flush=True)
if __name__=='__main__':
    import argparse
    parser=argparse.ArgumentParser()
    parser.add_argument('--hover-height-cm',type=float,default=.15)
    parser.add_argument('--output',default='unified-policy-evaluation.json')
    parser.add_argument('--landing',action='store_true')
    parser.add_argument('--retain-flight-attitude',action='store_true')
    parser.add_argument('--contact-handover',action='store_true')
    parser.add_argument('--stance-before-launch',action='store_true')
    parser.add_argument('--leg-ik',action='store_true')
    parser.add_argument('--claw-hold',action='store_true')
    parser.add_argument('--resume-walking',action='store_true')
    parser.add_argument('--walking-only',action='store_true')
    parser.add_argument('--recover-stance',action='store_true')
    parser.add_argument('--hold-seconds',type=float,default=.3)
    parser.add_argument('--hold-head',action='store_true',help='Diagnostic: retain held head actuator commands during walking resume')
    parser.add_argument('--filter-settle-seconds',type=float,default=0,help='Physical held-control settling after restoring walking actuator filters; no state reset')
    parser.add_argument('--claw-release-seconds',type=float,default=0,help='Diagnostic gradual release of existing claw adhesion during policy blend; no root force')
    parser.add_argument('--guard-leg-ik',action='store_true',help='Diagnostic physical foot-target recovery with joint posture regularization during safety fallback')
    parser.add_argument('--contact-guard',action='store_true',help='Engineering safety diagnostic: return toward held joint/claw commands when support or uprightness deteriorates')
    parser.add_argument('--match-resume-height',action='store_true',help='Diagnostic align synthetic height target with landed root height; no physical reset')
    parser.add_argument('--walking-blend-seconds',type=float,default=.25,help='Additional command interpolation before native policy; zero uses physical filters only')
    parser.add_argument('--restore-loaded-controls',action='store_true',help='Blend preflight loaded leg controls while releasing grip before native walking')
    parser.add_argument('--swing-posture-gain',type=float,default=0,help='Null-space bias toward measured preflight joint posture only for unloaded swing foot')
    parser.add_argument('--measured-stance-recovery',action='store_true',help='Recover measured loaded preflight body height and claw center heights')
    parser.add_argument('--replant-all-feet',action='store_true',help='Offline extend sequential physical replanting to all six legs')
    parser.add_argument('--complete-contacts',action='store_true',help='Lower unsupported feet through IK after a stable three-claw stance')
    parser.add_argument('--synchronize-wings',action='store_true',help='Match wing command phase to actual preflight wing angles without pose edits')
    parser.add_argument('--capsule-clearance',action='store_true',help='Include claw capsule projected half-length in floor IK target')
    parser.add_argument('--landing-pitch-degrees',type=float,default=None,help='Offline partial-pitch synthetic landing reference, blended over .6s; physical quaternion untouched')
    parser.add_argument('--loaded-stance-handover',action='store_true',help='Offline blend toward applied loaded preflight leg targets during wing shutdown, preserving support servo offsets')
    parser.add_argument('--heading-leg-ik',action='store_true',help='Offline landing foot targets follow actual root yaw rather than fixed preflight world heading')
    parser.add_argument('--automatic-recovery',action='store_true',help='Offline copied-physics lookahead selects native handoff or physical recovery; never advances real body')
    parser.add_argument('--adaptive-grip',action='store_true',help='Offline grip at symmetric rear support/up>=.45 or measured up>=.65, still requiring three claws/no body collision')
    parser.add_argument('--touchdown-recovery',action='store_true',help='Offline physical posture/foot support feedback starting at first three-claw grip')
    parser.add_argument('--replant-feet',action='store_true',help='Offline sequential front-foot physical repositioning under retained middle/rear support')
    parser.add_argument('--body-recovery',action='store_true',help='Offline leg-actuator recovery toward preflight thorax height and yaw-only body frame; no root force/reset')
    parser.add_argument('--balanced-grip',action='store_true',help='Offline require front and rear claw support before grip')
    parser.add_argument('--grip-max-speed',type=float,default=float('inf'),help='Offline maximum measured translation speed before claw grip, cm/s')
    parser.add_argument('--grip-min-up',type=float,default=-1,help='Offline minimum measured uprightness before contact grip; default preserves prior trigger')
    parser.add_argument('--resume-speed',type=float,default=.2,help='Offline diagnostic reference speed in cm/s; default retains prior .2')
    parser.add_argument('--walking-seconds',type=float,default=.5,help='Walking resume qualification duration; minimum .5 seconds')
    parser.add_argument('--command-slew-rate',type=float,default=0,help='Diagnostic non-claw actuator command slew limit per second; zero preserves native behavior')
    parser.add_argument('--seed',type=int,default=42)
    parser.add_argument('--wing-cut-seconds',type=float,default=.2)
    parser.add_argument('--stance-blend-seconds',type=float,default=.5)
    args=parser.parse_args()
    if not 0<=args.filter_settle_seconds<=.5:parser.error('Filter settling must be between zero and .5 seconds')
    if args.landing_pitch_degrees is not None and not 0<=args.landing_pitch_degrees<=47.5:parser.error('Landing pitch reference must lie in [0,47.5] degrees')
    if args.complete_contacts and not (args.leg_ik and args.claw_hold):parser.error('Contact completion requires IK and claw hold')
    if args.heading_leg_ik and not args.leg_ik:parser.error('Heading-relative targets require leg IK')
    if args.automatic_recovery and not (args.body_recovery and args.resume_walking):parser.error('Automatic recovery requires body recovery and walking resume')
    if args.touchdown_recovery and not (args.leg_ik and args.claw_hold):parser.error('Touchdown recovery requires IK and claw hold')
    if not 0<=args.walking_blend_seconds<=1:parser.error('Walking blend must lie in [0,1] seconds')
    if not 0<=args.swing_posture_gain<=.2:parser.error('Swing posture gain must lie in [0,.2]')
    if args.replant_all_feet and not args.replant_feet:parser.error('All-foot replant requires foot replant')
    if args.replant_feet and not args.body_recovery:parser.error('Foot replant requires body recovery')
    if args.body_recovery and not args.leg_ik:parser.error('Body recovery requires leg IK')
    if args.guard_leg_ik and not (args.contact_guard and args.leg_ik):parser.error('Guard leg IK requires contact guard and leg IK')
    if args.grip_max_speed<=0 or np.isnan(args.grip_max_speed):parser.error('Grip speed must be positive')
    if not -1<=args.grip_min_up<=1:parser.error('Grip trigger cosine must lie in [-1,1]')
    if not 0<args.resume_speed<=1:parser.error('Resume reference speed must be positive and at most 1 cm/s')
    if not .5<=args.walking_seconds<=6:parser.error('Walking duration must be between .5 and 6 seconds')
    if not 0<=args.command_slew_rate<=100:parser.error('Command slew rate must be between zero and 100')
    if not 0<=args.claw_release_seconds<=.5:parser.error('Claw release duration must be within zero and .5 seconds')
    if args.wing_cut_seconds<=0 or args.stance_blend_seconds<=0:parser.error('Ramp durations must be positive')
    main(args.hover_height_cm,args.output,args.landing,not args.retain_flight_attitude,args.contact_handover,args.stance_before_launch,args.leg_ik,args.claw_hold,args.resume_walking,args.seed,args.wing_cut_seconds,args.stance_blend_seconds,args.walking_only,args.recover_stance,args.hold_seconds,args.hold_head,args.filter_settle_seconds,args.claw_release_seconds,args.command_slew_rate,args.walking_seconds,args.match_resume_height,args.contact_guard,args.guard_leg_ik,args.resume_speed,args.grip_min_up,args.balanced_grip,args.body_recovery,args.replant_feet,args.grip_max_speed,args.touchdown_recovery,args.adaptive_grip,args.automatic_recovery,args.heading_leg_ik,args.loaded_stance_handover,args.landing_pitch_degrees,args.capsule_clearance,args.synchronize_wings,args.complete_contacts,args.replant_all_feet,args.measured_stance_recovery,args.swing_posture_gain,args.restore_loaded_controls,args.walking_blend_seconds)
