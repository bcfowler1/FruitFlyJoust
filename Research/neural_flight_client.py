"""Owned subprocess with separate TensorFlow/PyTorch environments and bounded IPC."""
import json, subprocess, threading, queue
from pathlib import Path
from dataclasses import asdict
import numpy as np
from experimental_coupling import BodyFeedback

class NeuralFlightClient:
    def __init__(self, worker='neural_flight_worker.py', log_name='neural-flight-worker.log',worker_args=()):
        root=Path(__file__).resolve().parents[3]
        self.log=(root/'work'/log_name).open('w')
        self.process=subprocess.Popen([str(root/'work/research-env/Scripts/python.exe'),
            str(Path(__file__).with_name(worker)),*worker_args],stdin=subprocess.PIPE,stdout=subprocess.PIPE,
            stderr=self.log,text=True,bufsize=1)
        self.responses=queue.Queue()
        def read():
            for line in self.process.stdout: self.responses.put(line)
            self.responses.put(None)
        threading.Thread(target=read,daemon=True).start()
        try:
            if not self.receive(120).get('ready'): raise RuntimeError('Neural worker not ready')
        except Exception: self.close(); raise
    def receive(self,timeout=30):
        try: line=self.responses.get(timeout=timeout)
        except queue.Empty: raise RuntimeError('Neural worker timed out')
        if line is None: raise RuntimeError('Neural worker stopped; inspect '+str(self.log.name))
        return json.loads(line)
    def send(self,value):
        self.process.stdin.write(json.dumps(value)+'\n'); self.process.stdin.flush()
        return self.receive()
    def step(self,physics,cues,seconds=.015):
        velocity=physics.data.qvel[:3]*.01
        feedback=BodyFeedback(float(np.linalg.norm(velocity)),float(physics.data.qpos[2])*.01,
            float(velocity[2]),float(physics.data.qvel[5]))
        return self.send(dict(cues=asdict(cues),feedback=asdict(feedback),steps=round(seconds/.0001)))
    def reset(self): return self.send(dict(reset=True))
    def close(self):
        if self.process.poll() is None:
            try: self.process.stdin.write('{"stop":true}\n'); self.process.stdin.flush(); self.process.wait(timeout=5)
            except (OSError,subprocess.TimeoutExpired): self.process.kill(); self.process.wait()
        self.log.close()
