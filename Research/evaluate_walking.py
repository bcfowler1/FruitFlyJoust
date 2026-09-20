"""Loaded gait, geometry, full-network coupling and synchronized reset qualification."""
import json,time
from pathlib import Path
import numpy as np
from walking_backend import WalkingBody
from neural_flight_client import NeuralFlightClient

def main():
    body=WalkingBody();client=None
    try:
        geometry=body.export();start=body.obs['fly'][0].copy();tick=time.perf_counter()
        for _ in range(100):body.step([.7,.7])
        displacement=body.obs['fly'][0]-start
        assert not body.ended and float(np.linalg.norm(displacement[:2]))>1
        assert np.isfinite(body.d.qpos).all()
        assert abs(body.m.body_mass.sum()-body.load['fly_mass_native']*4/3)<1e-9
        result=dict(geometry=geometry,rider_load=body.load,loaded_walk=dict(simulated_seconds=float(body.d.time),
            wall_seconds=time.perf_counter()-tick,displacement_mm=displacement.tolist(),finite=True))
        body.reset();assert abs(body.d.time)<1e-9
        client=NeuralFlightClient('neural_walking_worker.py','neural-walking-worker.log')
        samples=[]
        for _ in range(4):
            response=client.send(dict(turn=1,speed_mmps=float(np.linalg.norm(body.obs['fly'][1]))))
            rates=response['rates'];steering=np.clip((rates['right']-rates['left'])/100,-1,1)
            body.step(np.clip([.7+.28*steering,.7-.28*steering],0,1.5))
            assert abs(body.d.time-response['neural_time'])<1e-8
            samples.append(dict(body_time=float(body.d.time),brain=response))
        client.reset();body.reset();assert body.d.time==0
        response=client.send(dict(turn=1,speed_mmps=0));assert response['neural_time']==.015
        result.update(neural_samples=samples,brain_reset_passed=True,body_reset_passed=True,
            limitations=['synthetic direct descending stimulation','uncalibrated rate-to-gait steering',
                'engineering forward drive, no identified oDN1 mapping','walking only, no biological validation'])
        Path(__file__).with_name('walking-evaluation.json').write_text(json.dumps(result,indent=2))
        print(json.dumps(result,indent=2))
    finally:
        if client:client.close()
        body.close()

if __name__=='__main__':main()
