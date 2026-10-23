"""Assemble the private, self-contained x64 vehicle worker from isolated builds."""
import argparse, hashlib, json, shutil
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--compiler-root',type=Path,required=True);p.add_argument('--game-source',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--paint-effect',type=Path,required=True);a=p.parse_args()
root=a.compiler_root.resolve();game_source=a.game_source.resolve();out=a.output.resolve()
if out.is_relative_to(game_source):raise ValueError('Vehicle runtime must be outside game source')
expected={}
def copy(source,dest):
 if not source.is_file():raise FileNotFoundError(source)
 dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,dest)
 expected[str(dest.relative_to(out)).replace('\\','/')]=dest
for source in (root/'worker').rglob('*'):
 if source.is_file() and source.name not in ('bundle-manifest.json','manifest.json') and source.suffix.lower() not in ('.log','.pdb','.ilk'):copy(source,out/source.relative_to(root/'worker'))
copy(root/'bridge/bin/VehicleSceneBridge.exe',out/'Bin/VehicleSceneBridge.exe')
copy(root/'reader/bin/Compiler_r.exe',out/'Bin/VehicleSceneProbe.exe')
copy(a.paint_effect,out/'PaintSides.PFX')
copy(game_source/'PlayAll/External/Win32/Current/PlayAll/External/FbxSdk.2020.3.2/lib/vs2019/x64/release/libfbxsdk.dll',out/'Bin/libfbxsdk.dll')
if not (out/'Donors/catalog.json').is_file():raise FileNotFoundError('Prepare the donor pack before collecting the runtime')
catalog=json.loads((out/'Donors/catalog.json').read_text(encoding='utf-8-sig'))
for record in catalog:
 for name in [*record['Files'],'donor.json']:
  if Path(name).name!=name:raise ValueError('Invalid donor file')
  file=out/'Donors'/f"{record['Index']:02}"/name
  if not file.is_file():raise FileNotFoundError(file)
  expected[str(file.relative_to(out)).replace('\\','/')]=file
expected['Donors/catalog.json']=out/'Donors/catalog.json'
actual={str(f.relative_to(out)).replace('\\','/') for f in out.rglob('*') if f.is_file() and f.name!='bundle-manifest.json' and f!=out/'manifest.json'}
if actual!=set(expected):raise ValueError('Unexpected old runtime files; choose a fresh output folder: '+str(sorted(actual-set(expected))))
manifest={'Description':'Isolated x64 native geometry compiler, scene composer, engine reader, private dependencies and bundled donor source. End users need no compiler installation.', 'Files':[{'Path':name,'Bytes':f.stat().st_size,'SHA256':hashlib.sha256(f.read_bytes()).hexdigest().upper()} for name,f in sorted(expected.items())]}
(out/'bundle-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8');print(json.dumps({'files':len(manifest['Files']),'bytes':sum(f['Bytes'] for f in manifest['Files'])}))
