"""Offline closed-loop PFL3/connectome/hybrid-body engineering probe."""
import json,time
from pathlib import Path
import numpy as np
from walking_backend import WalkingBody
from walking_steering import WalkingSteering
from neural_flight_client import NeuralFlightClient

def main(neutral_only=False,edge_drives=False,motor_null=False,yaw_feedback_gain=.12,sensory_yaw_tau=0,middle_drive=False):
    started=time.perf_counter();trials=[]
    brain=NeuralFlightClient('neural_walking_worker.py','upstream-walking-body-worker.log',('--diagnostic-pfl3',))
    try:
        for drive,turn in ([(.7,t) for t in (-.5,0,.5)] if middle_drive else [(.9,0)] if motor_null else [(d,t) for d in (.5,.9) for t in (-.5,0,.5)] if edge_drives else [(.7,t) for t in ((0,) if neutral_only else (-.5,.5))]):
            brain.reset();body=WalkingBody(optimized=True,head_stabilization=True);controller=WalkingSteering(yaw_feedback_gain)
            try:
                origin=body.d.xpos[body.anchor].copy();samples=[];sensory_yaw=0.
                for step in range(60):
                    feedback=body.feedback();sensory_feedback=dict(feedback)
                    if sensory_yaw_tau:
                        sensory_yaw+=(1-np.exp(-.015/sensory_yaw_tau))*(feedback['yaw_rate_radps']-sensory_yaw)
                        sensory_feedback['yaw_rate_radps']=float(sensory_yaw)
                    frame=brain.send(dict(turn=turn,**sensory_feedback))
                    cue=controller.neural_cue(frame['rates'],turn,1)
                    action,state=controller.update(drive,0 if motor_null else cue,feedback['yaw_rate_radps'])
                    body.step(action)
                    samples.append(dict(time=float(body.d.time),neural_time=frame['neural_time'],
                        feedback=body.feedback(),sensory_feedback=sensory_feedback,rates=frame['rates'],neural_turn=cue,action=action.tolist(),
                        up=float(body.d.xmat[body.anchor].reshape(3,3)[2,2])))
                finite=bool(np.isfinite(body.d.qpos).all() and np.isfinite(body.d.qvel).all())
                distance=float(np.linalg.norm((body.d.xpos[body.anchor]-origin)[:2]))
                integrated_yaw=float(sum(s['feedback']['yaw_rate_radps']*.015 for s in samples))
                gates=dict(finite=finite,body_running=not body.ended,upright=min(s['up'] for s in samples)>.85,
                    locomotion=distance>.5,signed_yaw=abs(integrated_yaw)<.1 if turn==0 else integrated_yaw*turn>0,
                    synchronized_clocks=max(abs(s['time']-s['neural_time']) for s in samples)<1e-8)
                trials.append(dict(yaw_feedback_gain=yaw_feedback_gain,diagnostic_motor_null=motor_null,drive=drive,turn=turn,qualified=all(gates.values()),gates=gates,displacement_mm=distance,
                    integrated_yaw_rad=integrated_yaw,minimum_up=min(s['up'] for s in samples),
                    rider_load=body.load,stats=frame['stats'],samples=samples))
                print(json.dumps({k:v for k,v in trials[-1].items() if k!='samples'}),flush=True)
            finally:body.close()
    finally:brain.close()
    report=dict(sensory_yaw_tau=sensory_yaw_tau,qualified=all(t['qualified'] for t in trials),trials=trials,wall_seconds=time.perf_counter()-started,
        limits=dict(minimum_up=.85,minimum_displacement_mm=.5,maximum_clock_difference_seconds=1e-8,maximum_neutral_yaw_rad=.1),
        limitations='Short engineering trials. Synthetic upstream PFL3 excitation, existing uncalibrated DN-rate decoder, calibrated body steering and hybrid gait. Actual body feedback; one-third rider mass. Not sensory encoding, direct leg motor control, robust gameplay or biological validation.')
    filename='upstream-walking-motor-null-evaluation.json' if motor_null else 'upstream-walking-range-evaluation.json' if edge_drives else 'upstream-walking-neutral-evaluation.json' if neutral_only else 'upstream-walking-body-evaluation.json'
    if yaw_feedback_gain!=.12:
        filename=filename.replace('upstream-walking-', 'upstream-gain-'+str(yaw_feedback_gain)+'-')
    if middle_drive:filename=filename.replace('-body-', '-middle-')
    if sensory_yaw_tau:filename=filename.replace('-evaluation.json','-sensory-tau-'+str(sensory_yaw_tau)+'-evaluation.json')
    Path(__file__).with_name(filename).write_text(json.dumps(report,indent=2))
    print(json.dumps(dict(qualified=report['qualified'])),flush=True)

if __name__=='__main__':
    import argparse
    p=argparse.ArgumentParser();p.add_argument('--neutral-only',action='store_true');p.add_argument('--edge-drives',action='store_true');p.add_argument('--motor-null',action='store_true');p.add_argument('--yaw-feedback-gain',type=float,default=.12)
    p.add_argument('--middle-drive',action='store_true');p.add_argument('--sensory-yaw-tau',type=float,default=0);args=p.parse_args()
    if not 0<=args.sensory_yaw_tau<=1:p.error('Sensory yaw time constant must be within zero and one second')
    main(args.neutral_only,args.edge_drives,args.motor_null,args.yaw_feedback_gain,args.sensory_yaw_tau,args.middle_drive)
