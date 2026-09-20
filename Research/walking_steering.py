"""Qualified physical gait inverse with engineering angular feedback."""
import json,math
from pathlib import Path
import numpy as np

class WalkingSteering:
    def __init__(self,yaw_feedback_gain=.12):
        if not math.isfinite(yaw_feedback_gain) or not 0<=yaw_feedback_gain<=1:raise ValueError('Invalid yaw feedback gain')
        self.yaw_feedback_gain=yaw_feedback_gain
        self.fit=json.loads(Path(__file__).with_name('walking-calibration.json').read_text())
        if not self.fit['qualified']:raise RuntimeError('Walking fit failed qualification')
        self.coefficients=np.asarray(self.fit['coefficients']);self.reset()
    def reset(self):self.filtered_yaw=0.;self.integral=0.;self.neural_turn=0.
    def predict(self,s,t):return float(np.asarray([1,s,s*s,s*t,s*s*t,s*t*t*t])@self.coefficients)
    def update(self,drive,turn,yaw_rate,dt=.015):
        if not all(math.isfinite(v) for v in (drive,turn,yaw_rate,dt)):raise ValueError('Nonfinite steering')
        self.filtered_yaw+=(1-math.exp(-dt/.12))*(yaw_rate-self.filtered_yaw)
        inside=bool(.5<=drive<=.9)
        if inside:
            low,high=self.predict(drive,-.7),self.predict(drive,.7)
            goal=float(turn)*min(abs(low),abs(high))
            left,right=-.7,.7
            for _ in range(30):
                middle=(left+right)/2
                if self.predict(drive,middle)<goal:left=middle
                else:right=middle
            error=goal-self.filtered_yaw
            self.integral=float(np.clip(self.integral+error*dt*.1,-.2,.2))
            command=float(np.clip((left+right)/2+self.yaw_feedback_gain*error+self.integral,-.7,.7))
        else:goal=None;command=float(turn);self.integral=0.
        action=np.clip([drive*(1+.4*command),drive*(1-.4*command)],0,1.5)
        return action,dict(qualified_range=inside,target_yaw_radps=goal,filtered_yaw_radps=self.filtered_yaw,
            gait_turn=command,mode='measured physical inverse + engineering feedback' if inside else 'outside fit range: raw engineering drive')
    def neural_cue(self,rates,turn,influence,dt=.015):
        raw=float(np.clip((rates['right']-rates['left'])/100,-1,1))
        self.neural_turn+=(1-math.exp(-dt/.09))*(raw-self.neural_turn)
        return (1-influence)*turn+influence*self.neural_turn
