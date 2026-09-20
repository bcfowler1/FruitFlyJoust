"""Sequential, evidence-preserving qualification of the engineering floor loop."""
import argparse
from datetime import datetime,timezone
import hashlib
import json
from pathlib import Path
import subprocess
import sys


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--seeds',type=int,nargs='+',default=[46,42,43,44,45])
    parser.add_argument('--wing-cut-seconds',type=float,default=.2)
    parser.add_argument('--walking-blend-seconds',type=float,default=.25)
    parser.add_argument('--restore-loaded-controls',action='store_true')
    parser.add_argument('--claw-release-seconds',type=float,default=.25)
    parser.add_argument('--swing-posture-gain',type=float,default=0)
    parser.add_argument('--replant-all-feet',action='store_true')
    parser.add_argument('--measured-stance-recovery',action='store_true')
    parser.add_argument('--complete-contacts',action='store_true')
    parser.add_argument('--synchronize-wings',action='store_true')
    parser.add_argument('--capsule-clearance',action='store_true')
    parser.add_argument('--heading-leg-ik',action='store_true')
    parser.add_argument('--loaded-stance-handover',action='store_true')
    parser.add_argument('--stance-blend-seconds',type=float,default=.5)
    args=parser.parse_args()
    if not 0<args.wing_cut_seconds<=.5:parser.error('Wing shutdown must lie in (0,.5] seconds')
    if not 0<args.stance_blend_seconds<=1:parser.error('Stance blend must lie in (0,1] seconds')
    root=Path(__file__).resolve().parent
    run=datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
    prefix='unified-contact-qualification-'+run
    snapshot=root/'source-snapshots'/prefix;snapshot.mkdir(parents=True,exist_ok=False)
    hashes={}
    for name in ('evaluate_unified_policies.py','landing_leg_ik.py','handoff_prediction.py','qualify_unified_handoff.py'):
        content=(root/name).read_bytes();(snapshot/name).write_bytes(content)
        hashes[name]=hashlib.sha256(content).hexdigest()
    summary=dict(qualified=False,seeds_requested=args.seeds,experiments=[],source_sha256=hashes,
        controller_parameters=dict(wing_cut_seconds=args.wing_cut_seconds,stance_blend_seconds=args.stance_blend_seconds,
            grip_max_speed_cm_s=2,rear_pair_min_up=.45,other_grip_min_up=.65,walking_reference_cm_s=1,
            walking_blend_seconds=args.walking_blend_seconds,restore_loaded_controls=args.restore_loaded_controls,claw_release_seconds=args.claw_release_seconds,swing_posture_gain=args.swing_posture_gain,all_foot_replant=args.replant_all_feet,measured_stance_recovery=args.measured_stance_recovery,unsupported_contact_completion=args.complete_contacts,synchronized_wing_phase=args.synchronize_wings,capsule_floor_clearance=args.capsule_clearance,heading_relative_leg_ik=args.heading_leg_ik,loaded_stance_handover=args.loaded_stance_handover),
        limitations='Trained-policy and copied-physics engineering floor transitions; no connectome motor or biological qualification. No playable integration established by this offline suite.')
    summary_file=root/(prefix+'-summary.json')
    for seed in args.seeds:
        report=root/(prefix+'-seed'+str(seed)+'.json')
        log=report.with_suffix('.log')
        command=[sys.executable,str(root/'evaluate_unified_policies.py'),
            '--landing','--retain-flight-attitude','--stance-before-launch','--leg-ik','--claw-hold',
            '--resume-walking','--seed',str(seed),'--hold-seconds','2','--filter-settle-seconds','.1',
            '--claw-release-seconds',str(args.claw_release_seconds),'--walking-seconds','6','--resume-speed','1',
            '--grip-max-speed','2','--body-recovery','--replant-feet','--adaptive-grip',
            '--wing-cut-seconds',str(args.wing_cut_seconds),
            '--stance-blend-seconds',str(args.stance_blend_seconds),
            '--walking-blend-seconds',str(args.walking_blend_seconds),
            '--swing-posture-gain',str(args.swing_posture_gain),
            '--automatic-recovery','--output',report.name]
        if args.restore_loaded_controls:command.append('--restore-loaded-controls')
        if args.replant_all_feet:command.append('--replant-all-feet')
        if args.measured_stance_recovery:command.append('--measured-stance-recovery')
        if args.complete_contacts:command.append('--complete-contacts')
        if args.synchronize_wings:command.append('--synchronize-wings')
        if args.capsule_clearance:command.append('--capsule-clearance')
        if args.heading_leg_ik:command.append('--heading-leg-ik')
        if args.loaded_stance_handover:command.append('--loaded-stance-handover')
        print(json.dumps(dict(stage='qualification_started',seed=seed,report=report.name)),flush=True)
        with log.open('x',encoding='utf-8') as output:
            process=subprocess.run(command,stdout=output,stderr=subprocess.STDOUT,check=False)
        if process.returncode!=0 or not report.exists():
            entry=dict(seed=seed,qualified=False,process_exit_code=process.returncode,log=log.name,error='Evaluator did not complete normally')
        else:
            result=json.loads(report.read_text());walking=result.get('walking_resume',{})
            phase_constraints=bool(not args.synchronize_wings or result.get('wing_phase_alignment',{}).get('physical_state_unchanged') is True)
            physical_constraints=bool(phase_constraints and result.get('same_physics_instance') and result.get('root_external_force') is False
                and result.get('pose_handover_jump')==0 and result.get('velocity_handover_jump')==0
                and abs(result['rider_load']['mass_fraction']-1/3)<1e-12)
            constraints=bool(physical_constraints
                and walking.get('time_reset') is False and walking.get('pose_jump')==0
                and walking.get('velocity_jump')==0 and walking.get('completed_walking_seconds')==6
                and walking.get('native_handoff_prediction',{}).get('real_physical_state_unchanged') is True
                and result.get('landing',{}).get('contact_transition_completed') is True
                and walking.get('walking_limits')==dict(minimum_up_cosine=.85,minimum_displacement_cm=.01,
                    minimum_forward_displacement_cm=.01,minimum_final_quarter_second_forward_cm=.005))
            entry=dict(seed=seed,qualified=bool(result['qualified'] and constraints),report=report.name,
                physical_continuity_and_mass_verified=physical_constraints,
                continuity_mass_and_limits_verified=constraints,landing_qualified=result.get('landing',{}).get('qualified'),
                hold_qualified=walking.get('hold_qualified'),walking_seconds=walking.get('completed_walking_seconds'),
                minimum_walking_up=walking.get('minimum_walking_up_cosine'),forward_cm=walking.get('forward_displacement_cm'),
                final_quarter_forward_cm=walking.get('final_quarter_second_forward_cm'),
                selected_handoff=walking.get('native_handoff_prediction',{}).get('selection'))
        summary['experiments'].append(entry)
        summary['qualified']=bool(len(summary['experiments'])==len(args.seeds) and all(row['qualified'] for row in summary['experiments']))
        summary_file.write_text(json.dumps(summary,indent=2))
        print(json.dumps(dict(stage='qualification_finished',**entry,summary=summary_file.name)),flush=True)
        if not entry['qualified']:return 1
    return 0


if __name__=='__main__':sys.exit(main())
