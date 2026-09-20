"""Version-matched anatomical candidates; identities do not establish a flight decoder."""
import csv, hashlib, json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]
REVISION='8587524c1748ce5ef2080822a2fc890fc03bf597'
SOURCE='https://github.com/flyconnectome/flywire_annotations/tree/'+REVISION

def main():
    path=ROOT/'work/research/flywire-annotations-8587524.tsv'
    rows=[r for r in csv.DictReader(path.open(),delimiter='\t') if r['cell_type'].startswith('DNg02')]
    model_ids={r[''] for r in csv.DictReader((ROOT/'work/research/fly-brain/data/2025_Completeness_783.csv').open())}
    # Completeness file column spelling is checked rather than assuming float IDs.
    if not rows or any(r['root_id'] not in model_ids for r in rows):
        raise RuntimeError('Candidate roots do not match the v783 simulation')
    groups={side:[r['root_id'] for r in rows if r['side']==side] for side in ['left','right']}
    report=dict(dataset_version='783',annotation_source=SOURCE,annotation_commit=REVISION,
        annotation_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
        candidate='DNg02',decoder_enabled=False,calibration=None,
        evidence='https://pmc.ncbi.nlm.nih.gov/articles/PMC9206711/',
        populations=groups,neurons=[{k:r[k] for k in ['root_id','cell_type','side','flow','super_class']} for r in rows],
        limitation='Anatomical identity and published amplitude/steering evidence do not provide a quantitative decoder or pitch control.')
    Path(__file__).with_name('flight-populations.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(dict(count=len(rows),sides={k:len(v) for k,v in groups.items()},model_membership=True)))

if __name__=='__main__': main()
