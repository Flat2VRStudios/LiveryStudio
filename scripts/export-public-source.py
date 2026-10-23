"""Export a committed, audited source ZIP without any development Git history."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import zipfile

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--revision',required=True)
parser.add_argument('--output',type=Path,required=True)
args=parser.parse_args()
root=Path(__file__).resolve().parents[1]
revision=subprocess.run(['git','-C',str(root),'rev-parse','--verify',args.revision+'^{commit}'],check=True,capture_output=True,text=True).stdout.strip()
args.output.mkdir(parents=True,exist_ok=True)
target=args.output/'LiveryStudio-0.1.0-source.zip'
if target.exists():raise ValueError('Choose a fresh output; existing source packages are retained')
with tempfile.TemporaryDirectory(prefix='livery-source-') as folder:
    folder=Path(folder);raw=folder/'tree.zip';tree=folder/'source'
    subprocess.run(['git','-C',str(root),'archive','--format=zip','--output='+str(raw),revision],check=True)
    with zipfile.ZipFile(raw) as archive:
        for entry in archive.infolist():
            if not (tree/entry.filename).resolve().is_relative_to(tree.resolve()):raise ValueError('Unsafe source archive path')
        archive.extractall(tree)
    spec=importlib.util.spec_from_file_location('public_audit',root/'scripts/audit-public.py');module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    result=module.audit(tree)
    if not result['Passed']:raise ValueError(json.dumps(result))
    inventory={}
    with zipfile.ZipFile(target,'x',zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
        for path in sorted(tree.rglob('*')):
            if path.is_file():
                name='LiveryStudio-0.1.0/'+path.relative_to(tree).as_posix()
                inventory[name]=hashlib.sha256(path.read_bytes()).hexdigest()
                archive.write(path,name)
    with zipfile.ZipFile(target) as archive:
        if len(archive.infolist())!=len(inventory) or set(archive.namelist())!=set(inventory):raise ValueError('Unexpected source ZIP members')
        for name,expected in inventory.items():
            if hashlib.sha256(archive.read(name)).hexdigest()!=expected:raise ValueError('Source ZIP content mismatch: '+name)
with target.open('rb') as stream:digest=hashlib.file_digest(stream,'sha256').hexdigest().upper()
record={'Revision':revision,'Archive':target.name,'Bytes':target.stat().st_size,'SHA256':digest,'NoGitHistory':True,'EveryArchivedFileMatches':True,**result}
(args.output/'source-verification.json').write_text(json.dumps(record,indent=2)+'\n')
(args.output/'SHA256SUMS-source.txt').write_text(digest+'  '+target.name+'\n')
print(json.dumps(record,indent=2))
