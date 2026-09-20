"""Paired engineering motor-influence test; never biological qualification."""
import json,time
from pathlib import Path
from serve_flight import create_environment,ASSETS,tf,np
from optimized_flight_policy import CompiledMeanPolicy
from neural_flight_client import NeuralFlightClient
from brain_body_interface import RiderCues
from rider_load import RiderLoad
from experimental_coupling import decode_rates
from flight_motor_controller import motor_action

def trial(policy,neural,index,influence,zero_readout=False):
    neural.reset()
    env=create_environment(index)
    try:
        timestep=env.reset();load=RiderLoad(env.physics,1/3).apply(env.physics)
        spec=env.action_spec();wings=env.task._wing_inds_action
        residual=dict(left=0.,right=0.);errors=[];heights=[];clipped=0;applied=0.;clock_error=0.;rates=0.
        for frame in range(2000):
            before=float(env.physics.data.time)
            for step in range(75):
                obs={k:tf.convert_to_tensor(v[None],dtype=tf.float32) for k,v in timestep.observation.items()}
                raw=np.asarray(policy(obs).mean())[0].copy()
                action=motor_action(raw,env.task,residual,influence)
                for side,indices in zip(('left','right'),(wings[:3],wings[3:])):
                    delta=residual[side]*influence;applied=max(applied,abs(delta))
                if not np.isfinite(action).all():raise RuntimeError('nonfinite action')
                clipped+=int(np.sum((action<spec.minimum)|(action>spec.maximum)))
                timestep=env.step(np.clip(action,spec.minimum,spec.maximum).copy())
                if not np.isfinite(env.physics.data.qpos).all() or not np.isfinite(env.physics.data.qvel).all():raise RuntimeError('nonfinite body state')
                errors.append(float(np.linalg.norm(env.task.observables['walker/ref_displacement'](env.physics)[0]))*.01)
                heights.append(float(env.task._walker.observables.thorax_height(env.physics))*.01)
                if timestep.last():break
            elapsed=float(env.physics.data.time)-before
            state=neural.step(env.physics,RiderCues(turn=.5,climb=.2),elapsed)
            residual=decode_rates(dict(left=0.,right=0.)) if zero_readout else state['residual']
            rates=max(rates,sum(state['rates'].values()))
            clock_error=max(clock_error,abs(state['neural_time']-float(env.physics.data.time)))
            if timestep.last():break
        physical=env.physics.data.qpos.copy()
        result=dict(trajectory=index,influence=influence,zero_readout_ablation=zero_readout,reference_completed=bool(env.task._reached_traj_end),
            simulated_seconds=float(env.physics.data.time),minimum_thorax_height_m=min(heights),maximum_tracking_error_m=max(errors),
            clipped_actions=clipped,maximum_applied_residual=applied,maximum_population_rate_hz=rates,maximum_clock_error_seconds=clock_error,rider_load=load)
        result['flight_limits_passed']=bool(result['simulated_seconds']>=.1 and min(heights)>=.002 and max(errors)<.02 and clipped==0)
        print(json.dumps(dict(stage='paired_neural_trial',**result)),flush=True)
        return result,physical
    finally:env.close()

def main():
    path=Path(__file__).with_name('neural-flight-causality-'+time.strftime('%Y%m%dT%H%M%SZ',time.gmtime())+'-evaluation.json')
    report=dict(engineering_qualified=False,biologically_validated=False,controller='Neutral native neck position target; unchanged trained wing/abdomen/frequency policy plus experimental neural wing residuals',tests=[],limits='Existing trained-flight limits: >=.1 s, height >=.002 m, tracking error <.02 m, no action clipping. Synthetic descending excitation and arbitrary decoder gains; trained policy remains stabilizer.')
    try:
        policy=CompiledMeanPolicy(tf.saved_model.load(str(ASSETS/'flight')))
        neural=NeuralFlightClient(log_name='neural-flight-causality-worker.log')
        try:
            for index in (0,1,2):
                off,qoff=trial(policy,neural,index,0.)
                on,qon=trial(policy,neural,index,1.)
                null,qnull=trial(policy,neural,index,1.,zero_readout=True)
                repeat,qrepeat=trial(policy,neural,index,1.)
                difference=float(np.max(np.abs(qon-qoff)))
                ablation_difference=float(np.max(np.abs(qon-qnull)))
                repeat_difference=float(np.max(np.abs(qon-qrepeat)))
                repeatable=bool(np.allclose(qon,qrepeat,rtol=1e-6,atol=1e-8))
                causal=bool(on['maximum_population_rate_hz']>0 and on['maximum_applied_residual']>0 and difference>1e-10 and ablation_difference>1e-10 and repeatable)
                passed=bool(causal and all(t['flight_limits_passed'] and t['reference_completed'] and t['maximum_clock_error_seconds']<1e-7 for t in (off,on,null,repeat)))
                report['tests'].append(dict(disabled=off,enabled=on,zero_readout=null,repeat_enabled=repeat,maximum_final_qpos_difference=difference,maximum_zero_readout_difference=ablation_difference,maximum_repeat_difference=repeat_difference,spike_readout_motor_effect_verified=causal,passed=passed))
                path.write_text(json.dumps(report,indent=2)+'\n')
            report['engineering_qualified']=all(t['passed'] for t in report['tests'])
        finally:neural.close()
    except Exception as error:report['error']=repr(error)
    path.write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(dict(report=path.name,**report)),flush=True)
    if not report['engineering_qualified']:raise SystemExit(1)

if __name__=='__main__':main()
