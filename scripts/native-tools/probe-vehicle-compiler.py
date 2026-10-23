"""Probe the bundled legacy geometry compiler in an isolated scratch project.

Never installs outputs or changes the input runtime/game. Exit 0 means the
probe completed, not that native vehicle support passed: inspect each case.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
import xml.etree.ElementTree as ET
import zipfile


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--runtime', type=Path, required=True)
    parser.add_argument('--archive', type=Path, required=True)
    parser.add_argument('--workspace', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--representatives',action='store_true')
    parser.add_argument('--reader',type=Path)
    parser.add_argument('--include-scenes',action='store_true')
    args = parser.parse_args()
    workspace = args.workspace.resolve()
    workspace.mkdir(parents=True, exist_ok=False)
    runtime = workspace / 'Runtime'
    shutil.copytree(args.runtime, runtime, ignore=shutil.ignore_patterns('*.log'))
    fallback = runtime / 'Compatibility/d3d9.dll'
    if fallback.is_file():
        shutil.copy2(fallback, runtime / 'Bin/d3d9.dll')
    data = workspace / 'Project/Data'
    assets = data / 'Assets/GRAPH/CARS/DERBY/BUICK'
    assets.mkdir(parents=True)
    (data / 'WIN32').mkdir()
    (data / 'Settings').mkdir()
    (data / 'COMMON/ENGINE/ARCHETYPES').mkdir(parents=True)
    (data / 'COMMON/ARCHETYPES').mkdir(parents=True)
    settings=runtime/'Settings'
    if settings.is_dir():
        for setting in settings.glob('*.XML'):
            shutil.copy2(setting,data/'Settings'/setting.name)
    repo = Path(__file__).resolve().parents[2]
    if not (data/'Settings/TEXTURE.SETTINGS.XML').is_file():
        shutil.copy2(repo / 'LiveryStudio/Core/Templates/TEXTURE_SETTINGS.txt', data / 'Settings/TEXTURE.SETTINGS.XML')
    inputs = []
    cases=[]
    with zipfile.ZipFile(args.archive) as archive:
        wanted=('Cars/DERBY/Buick/',)
        if args.representatives:wanted+=('Cars/CLASSIC/Buick/','Cars/ALLSTARS/Buick_Evo/','Cars/CLASSIC/Icecream_Van/','Cars/ALLSTARS/Jeep_Evo/')
        for name in archive.namelist():
            if not name.startswith(wanted) or not name.upper().endswith(('.FBX','.P3D.XML','.PSI.XML')):continue
            target=data/'Assets/GRAPH'/name
            if not target.resolve().is_relative_to((data/'Assets').resolve()):raise ValueError('Invalid archive member path')
            target.parent.mkdir(parents=True,exist_ok=True)
            target.write_bytes(archive.read(name))
            inputs.append({'archiveMember':name,'sha256':sha(target)})
            if name.upper().endswith('.P3D.XML') or args.include_scenes and name.upper().endswith('.PSI.XML'):
                cases.append('GRAPH/'+name)
        # Preserve the original minimal probe location/case for its diagnostic.
        for stem in ('Buick','Buick_Wheel'):
            for extension in ('.FBX','.P3D.XML'):
                name=f'Cars/DERBY/Buick/{stem}{extension}'
                target=assets/(stem+extension)
                target.write_bytes(archive.read(name))
    # A diagnostic case isolates geometry import from collision cooking. It is
    # not a replacement for the unchanged donor's full physics compilation.
    diagnostic = ET.parse(assets / 'Buick.P3D.XML')
    for p3d in diagnostic.getroot().iter('p3d'):
        for physics in list(p3d.findall('physics')):
            p3d.remove(physics)
    diagnostic.write(assets / 'Buick_NoPhysics.P3D.XML', encoding='utf-8', xml_declaration=True)
    if not args.representatives:cases.append('GRAPH/CARS/DERBY/BUICK/Buick_NoPhysics.P3D.XML')
    longest=max((str(path) for path in (data/'Assets').rglob('*') if path.is_file()),key=len)
    if len(longest)>=260:
        raise ValueError('The legacy compiler needs a shorter job workspace; source path exceeds MAX_PATH: '+longest)
    if args.reader:shutil.copy2(args.reader,runtime/'Bin/VehicleSceneProbe.exe')
    report = {'scope': 'Bundled compiler probe only; no install or in-game validation',
              'workspace': str(workspace), 'inputRuntime': str(args.runtime.resolve()),
              'compatibilityRuntime': fallback.is_file(), 'inputs': inputs,
              'runtimeFiles': {str(p.relative_to(runtime)): sha(p) for p in runtime.rglob('*') if p.is_file()},
              'cases': []}
    # Compile geometry before its scene instance.
    cases.sort(key=lambda path:(path.upper().endswith('.PSI.XML'),path.upper()))
    for relative in cases:
        asset = 'ASSETS:/'+relative
        command = [str(runtime / 'Bin/Compiler_r.exe'), '/Input', asset, '/Platform', 'PC', '/ProjectPath', str(data.parent)]
        started = time.monotonic()
        env = dict(os.environ, DXVK_LOG_LEVEL='none')
        try:
            result = subprocess.run(command, cwd=runtime / 'Bin', env=env, capture_output=True,
                                    timeout=35, creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
            case = {'asset': asset, 'exitCode': result.returncode,
                    'stdout': result.stdout.decode('utf-8', errors='replace'),
                    'stderr': result.stderr.decode('utf-8', errors='replace')}
        except subprocess.TimeoutExpired as error:
            case = {'asset': asset, 'timedOut': True,
                    'stdout': (error.stdout or b'').decode('utf-8', errors='replace'),
                    'stderr': (error.stderr or b'').decode('utf-8', errors='replace')}
        case['seconds'] = round(time.monotonic() - started, 3)
        case['outputs'] = {str(p.relative_to(data / 'WIN32')): {'bytes': p.stat().st_size, 'sha256': sha(p)}
                           for p in (data / 'WIN32').rglob('*') if p.is_file() and p.suffix.upper() in ('.P3D', '.PSI')}
        expected = relative[:-4].replace('\\', '/').upper()
        matches = [(path, metadata) for path, metadata in case['outputs'].items()
                   if path.replace('\\', '/').upper() == expected]
        case['compiledOutput'] = {'path': matches[0][0], **matches[0][1]} if len(matches) == 1 else None
        if args.reader and case.get('exitCode')==0 and relative.upper().endswith('.P3D.XML'):
            try:
                read=subprocess.run([str(runtime/'Bin/VehicleSceneProbe.exe'),str(data/'WIN32'),relative[:-4]],cwd=runtime/'Bin',capture_output=True,timeout=35,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
                case['nativeRead']={'exitCode':read.returncode,'stdout':read.stdout.decode('utf-8',errors='replace'),'stderr':read.stderr.decode('utf-8',errors='replace')}
            except subprocess.TimeoutExpired as error:
                case['nativeRead']={'timedOut':True,'stdout':(error.stdout or b'').decode('utf-8',errors='replace'),'stderr':(error.stderr or b'').decode('utf-8',errors='replace')}
        report['cases'].append(case)
        args.output.write_text(json.dumps(report, indent=2), encoding='utf-8')
        print(json.dumps({k:v for k,v in case.items() if k not in ('stdout','stderr','nativeRead','outputs','compiledOutput')} |
                         {'outputCount':len(case['outputs']),'nativeReadExit':case.get('nativeRead',{}).get('exitCode')} |
                         {'errors':[line for line in case['stderr'].splitlines() if line.startswith('ERROR:')]}),flush=True)


if __name__ == '__main__':
    main()
