"""Export the installed FlyGym head MLP unchanged; verify CPU inference parity."""
from pathlib import Path
import pickle,json,hashlib
import numpy as np,torch
ROOT=Path(__file__).resolve().parents[3]
source=ROOT/'work/neuromechfly-env/Lib/site-packages/flygym/data/trained_models/head_stabilization'
checkpoint=source/'all_dofs_model.ckpt'
state=torch.load(checkpoint,map_location='cpu',weights_only=False)['state_dict']
with (source/'joint_angle_scaler_params.pkl').open('rb') as stream:scaler=pickle.load(stream)
arrays={name.replace('.','_'):state[name].numpy() for name in state if name.startswith(('layer1.','layer2.','layer3.'))}
arrays.update(mean=scaler['mean'],std=scaler['std'])
rng=np.random.RandomState(42);x=rng.normal(size=(1000,48)).astype(np.float32);a=x.copy();b=torch.from_numpy(x)
for i in (1,2,3):
    weight=arrays[f'layer{i}_weight'];bias=arrays[f'layer{i}_bias']
    a=a@weight.T+bias;b=torch.nn.functional.linear(b,torch.from_numpy(weight),torch.from_numpy(bias))
    if i<3:a=np.maximum(a,0);b=torch.relu(b)
error=float(np.max(np.abs(a-b.numpy())));assert error<2e-6,error
np.savez(Path(__file__).with_name('head-controller.npz'),**arrays)
result=dict(qualified=True,maximum_numpy_torch_difference=error,samples=1000,
    source='FlyGym 1.1 bundled head-stabilization MLP; Apache-2.0',
    checkpoint_sha256=hashlib.sha256(checkpoint.read_bytes()).hexdigest(),unchanged_weights=True)
Path(__file__).with_name('head-controller-parity.json').write_text(json.dumps(result,indent=2));print(result)
