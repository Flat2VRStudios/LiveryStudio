"""Package a prepared, authorized Windows folder and verify every ZIP member."""
import argparse
import contextlib
import hashlib
import json
from pathlib import Path
import re
import tempfile
import zipfile

def sha(path):
    with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest().upper()

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--published',type=Path,required=True)
parser.add_argument('--output',type=Path,required=True)
parser.add_argument('--revision',required=True)
parser.add_argument('--launcher',type=Path,default=Path(__file__).resolve().parents[1]/'LiveryStudio/artifacts/launcher/LiveryStudio.exe')
parser.add_argument('--unpacked',type=Path,help='Retain a verified extraction in a new folder')
args=parser.parse_args()
root=args.published.resolve();args.output.mkdir(parents=True,exist_ok=True)
if args.output.resolve().is_relative_to(root):raise ValueError('Release output must be outside the published folder')
if not re.fullmatch('[0-9a-fA-F]{40}',args.revision):raise ValueError('Use the exact source commit SHA')
launcher=args.launcher.resolve()
if not launcher.is_file() or launcher.is_symlink() or launcher==root/'LiveryStudio.exe':raise ValueError('Build the separate native launcher first')
if args.unpacked and args.unpacked.exists():raise ValueError('Choose a new unpacked folder; existing files are retained')
for name in ('LiveryStudio.exe','LiveryStudio.dll','LiveryStudio.Core.dll','LICENSE','THIRD_PARTY_NOTICES.md','README.md','ufbx-LICENSE.txt','Assets/licenses/StbImageSharp-LICENSE.txt','Workshop/Compatibility/DXVK-LICENSE.txt'):
    if not (root/name).is_file():raise ValueError('Missing release file: '+name)
files=sorted(p for p in root.rglob('*') if p.is_file())
for path in files:
    relative=path.relative_to(root)
    if path.is_symlink() or not path.resolve().is_relative_to(root) or path.suffix.lower() in ('.pdb','.ilk','.log','.lib','.exp','.rdc','.flproject','.flcar','.flart') or path.name.lower() in ('steam_appid.txt','steam_appid64.txt','receipt.json','current.json'):
        raise ValueError('Private/debug output in release: '+relative.as_posix())
    if path.suffix.lower() in ('.json','.txt','.md','.xml','.cfg','.manifest'):
        try:text=path.read_text(encoding='utf-8-sig')
        except UnicodeError:continue
        if re.search(r'[A-Z]:[/\\](?:Users|Github|RQ-Engine)[/\\]',text,re.I):raise ValueError('Private build path in release: '+relative.as_posix())
for folder,manifest in (('Workshop','bundle-manifest.json'),('Preview','asset-manifest.json'),('VehicleTools','bundle-manifest.json')):
    data=json.loads((root/folder/manifest).read_text(encoding='utf-8-sig'))
    for entry in data if isinstance(data,list) else data['Files']:
        asset=(root/folder/entry['Path']).resolve()
        if not asset.is_relative_to(root/folder) or not asset.is_file() or sha(asset)!=entry['SHA256'].upper():raise ValueError('Invalid payload manifest: '+folder+'/'+entry['Path'])
members={('README.md' if p.relative_to(root).as_posix()=='README.md' else 'App/'+p.relative_to(root).as_posix()):p for p in files}
members['LiveryStudio.exe']=launcher
inventory={name:sha(path) for name,path in members.items()}
target=args.output/'LiveryStudio-0.1.0-Windows-x64.zip'
if target.exists():raise ValueError('Choose a fresh output; existing packages are retained')
with zipfile.ZipFile(target,'x',zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
    for name,path in members.items():archive.write(path,name)
if args.unpacked:args.unpacked.mkdir(parents=True)
with (contextlib.nullcontext(args.unpacked) if args.unpacked else tempfile.TemporaryDirectory(prefix='livery-release-check-')) as temporary:
    temporary=Path(temporary).resolve()
    with zipfile.ZipFile(target) as archive:
        if len(archive.infolist())!=len(inventory) or len(set(archive.namelist()))!=len(inventory):raise ValueError('Duplicate/missing package member')
        for entry in archive.infolist():
            dest=(temporary/entry.filename).resolve()
            if not dest.is_relative_to(temporary):raise ValueError('Unsafe release member')
            archive.extract(entry,temporary)
            if sha(dest)!=inventory[entry.filename]:raise ValueError('Extracted file mismatch: '+entry.filename)
record={'Version':'0.1.0','Revision':args.revision,'Archive':target.name,'Bytes':target.stat().st_size,'SHA256':sha(target),'Files':len(members),'Layout':'LiveryStudio.exe, README.md, App/','EveryExtractedFileMatches':True,'ManifestsMatch':True,'Signed':False,'CleanPcTest':False,'SteamDrivingTest':False}
(args.output/'release-verification.json').write_text(json.dumps(record,indent=2)+'\n')
(args.output/'files-sha256.json').write_text(json.dumps(inventory,indent=2)+'\n')
(args.output/'SHA256SUMS-windows.txt').write_text(record['SHA256']+'  '+target.name+'\n')
print(json.dumps(record,indent=2))
