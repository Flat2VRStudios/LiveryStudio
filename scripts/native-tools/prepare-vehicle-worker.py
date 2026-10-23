"""Assemble a matching x64 vehicle compiler spike with dependency provenance."""
import argparse
import collections
import hashlib
import json
import os
from pathlib import Path
import shutil
from native_pe import inspect_pe
from native_manifest import private_crt_manifest, rebind_staged_crt


# Windows 10/11 platform DLLs. An arbitrary DLL found in System32 is not proof
# that a clean PC supplies it (VC/DirectX redistributables often live there).
WINDOWS_DLLS = {
    'advapi32.dll', 'd3d11.dll', 'd3d9.dll', 'd3dcompiler_47.dll', 'dbghelp.dll',
    'dinput8.dll', 'dsound.dll', 'dwmapi.dll', 'dxgi.dll', 'gdi32.dll', 'imm32.dll',
    'kernel32.dll', 'msacm32.dll', 'msvcrt.dll', 'ole32.dll', 'oleaut32.dll',
    'shell32.dll', 'user32.dll', 'version.dll', 'winhttp.dll', 'winmm.dll', 'ws2_32.dll',
}


def main():
    p=argparse.ArgumentParser()
    p.add_argument('--game-source',type=Path,required=True);p.add_argument('--compiler',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True)
    p.add_argument('--importer',type=Path)
    p.add_argument('--writer',type=Path)
    p.add_argument('--physics-cooker',type=Path)
    p.add_argument('--crt90', type=Path, required=True, help='Original x64 VC90 CRT DLL directory')
    p.add_argument('--crt90-manifest', type=Path, required=True, help='Matching original CRT assembly manifest')
    args=p.parse_args();game_source=args.game_source.resolve();out=args.output.resolve()
    out.mkdir(parents=True,exist_ok=False);binary=out/'Bin';plugins=binary/'ContentPipeline';plugins.mkdir(parents=True)
    roots=[game_source/'PlayAll/Libs/vs2022/x64',game_source/'PlayAll/Redist/AssetCompiler',
           game_source/'PlayAll/Tools/Bin/Editor',game_source/'Flatout/Bin/x64',
           game_source/'PlayAll/External/Win32/Current/PlayAll/External']
    # Developer packaging uses an installed VS redistributable. The staged
    # worker, unlike this source build step, never searches Visual Studio.
    vs_root=Path(os.environ['ProgramFiles'])/'Microsoft Visual Studio/2022'
    crt143=list(vs_root.glob('*/VC/Redist/MSVC/*/x64/Microsoft.VC143.CRT'))
    roots+=sorted(crt143,key=lambda path:tuple(int(x) for x in path.parents[1].name.split('.')),reverse=True)
    candidates=collections.defaultdict(list)
    for root in roots:
        if not root.exists():continue
        for dll in root.rglob('*.dll'):
            try:
                if inspect_pe(dll)[0]==0x8664:candidates[dll.name.lower()].append(dll)
            except (ValueError,IndexError):pass
    # The vendor's x64 import library requests fsbank.dll, while its matching
    # redistributable is named fsbank64.dll. Stage only the inspected x64 DLL.
    if not candidates['fsbank.dll'] and candidates['fsbank64.dll']:
        candidates['fsbank.dll']=candidates['fsbank64.dll']
    # Legacy DirectX and OpenMP are redistributable runtimes, not guaranteed
    # Windows components. Include their actual x64 modules in this probe.
    legacy={'d3dx9_43.dll','d3dx11_43.dll','d3dcompiler_43.dll','xinput1_3.dll','vcomp140.dll'}
    for name in legacy:
        dll=Path(os.environ['WINDIR'])/'System32'/name
        if dll.is_file() and inspect_pe(dll)[0]==0x8664:candidates[name].append(dll)
    manifest={};pending=collections.deque()
    def copy(source,destination,inspect=True):
        destination.parent.mkdir(parents=True,exist_ok=True)
        shutil.copy2(source,destination)
        manifest[destination.relative_to(out).as_posix()]={'source':str(source.resolve()),'sha256':hashlib.sha256(source.read_bytes()).hexdigest().upper()}
        if inspect:pending.append(destination)
    copy(args.compiler,binary/'Compiler_r.exe')
    (out/'Settings').mkdir()
    for setting in (game_source/'Flatout/Data/Settings').glob('*.XML'):
        shutil.copy2(setting,out/'Settings'/setting.name)
        manifest['Settings/'+setting.name]={'source':str(setting.resolve()),'sha256':hashlib.sha256(setting.read_bytes()).hexdigest().upper()}
    for name in ('Importer64_r.dll','Writer64_r.dll','WriterWin3264_r.dll'):
        override=args.importer if name=='Importer64_r.dll' else args.writer if name=='Writer64_r.dll' else None
        copy(override or game_source/'PlayAll/Redist/AssetCompiler/ContentPipeline'/name,plugins/name)
    if args.physics_cooker:
        # ShapeGenerator's existing virtual path is unsuffixed even for x64.
        copy(args.physics_cooker,plugins/'WriterPhysX_r.dll')
    crt_manifest,crt_version=private_crt_manifest(args.crt90_manifest.read_bytes())
    private_crt=binary/'Microsoft.VC90.CRT'
    private_crt.mkdir()
    for name in ('msvcr90.dll','msvcp90.dll'):
        source=args.crt90/name
        if inspect_pe(source)[0]!=0x8664:raise ValueError('Expected x64 CRT: '+str(source))
        copy(source,private_crt/name)
    manifest_path=private_crt/'Microsoft.VC90.CRT.manifest'
    manifest_path.write_bytes(crt_manifest)
    manifest[manifest_path.relative_to(out).as_posix()]={
        'source':str(args.crt90_manifest.resolve()),
        'sourceSha256':hashlib.sha256(args.crt90_manifest.read_bytes()).hexdigest().upper(),
        'sha256':hashlib.sha256(crt_manifest).hexdigest().upper(),
        'transformation':'Private native-only CRT assembly identity; native vendor file hashes retained',
    }
    missing=[];system=[];visited=set()
    visited.update(('msvcr90.dll','msvcp90.dll','msvcm90.dll'))
    while pending:
        module=pending.popleft()
        for name in inspect_pe(module)[1]:
            key=name.lower()
            if key in visited:continue
            visited.add(key)
            if key in candidates:copy(candidates[key][0],binary/name)
            elif key.startswith(('api-ms-','ext-ms-')) or key in WINDOWS_DLLS:system.append(name)
            else:missing.append(name)
    tootle=binary/'TootleSoftwareOnly_2k8_64.dll'
    if not tootle.is_file():raise ValueError('Expected the writer\'s Tootle dependency')
    tootle_entry=manifest[tootle.relative_to(out).as_posix()]
    tootle_entry['sourceSha256']=tootle_entry['sha256']
    tootle_entry['manifestLanguages']=rebind_staged_crt(tootle,crt_version)
    tootle_entry['sha256']=hashlib.sha256(tootle.read_bytes()).hexdigest().upper()
    tootle_entry['transformation']='Bind copied DLL manifest to bundled private VC90 CRT; no code changes'
    report={'scope':'x64 vehicle compiler spike, not installed into the app',
            'files':manifest,'systemDependencies':sorted(system),'missingDependencies':sorted(missing),
            'crt90Version':crt_version,'cleanPcValidated':False,
            'redistributionInputs':'Developer-selected source runtimes; shipping provenance review remains separate'}
    (out/'manifest.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps({'files':len(manifest),'bytes':sum((out/n).stat().st_size for n in manifest),'missing':missing},indent=2))
    if missing:raise SystemExit(2)


if __name__=='__main__':main()
