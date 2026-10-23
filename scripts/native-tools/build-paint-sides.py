"""Compile the dual-UV paint effect in a private copy of the native worker.

Build WriterWin32 with build-vehicle-compiler.py first. Only developer packaging
needs shader sources or this writer; the app ships the compiled effect.
"""
import argparse, shutil, subprocess
from pathlib import Path

p=argparse.ArgumentParser()
p.add_argument('--game-source',type=Path,required=True)
p.add_argument('--runtime',type=Path,required=True)
p.add_argument('--writer',type=Path,required=True)
p.add_argument('--output',type=Path,required=True)
a=p.parse_args()
repo=Path(__file__).resolve().parents[2]
job=a.output.resolve()
if job.exists():raise ValueError('Choose a fresh output folder')
if job.is_relative_to(a.game_source.resolve()):raise ValueError('Keep generated assets outside the game checkout')
bin=job/'Bin';shutil.copytree(a.runtime/'Bin',bin,ignore=shutil.ignore_patterns('*.pdb','*.ilk','*.log'))
shutil.copyfile(a.writer,bin/'ContentPipeline/WriterWIN32_r.dll')
data=job/'Data'
shutil.copytree(a.game_source/'Flatout/Data/Assets/Engine/Effects',data/'Assets/Engine/Effects')
import importlib.util
spec=importlib.util.spec_from_file_location('paint_sides',Path(__file__).with_name('prepare-paint-sides.py'));generator=importlib.util.module_from_spec(spec);spec.loader.exec_module(generator)
generator.prepare(a.game_source,data/'Assets/Effects')
shutil.copytree(a.runtime/'Settings',data/'Settings')
for name in ['WIN32','COMMON/ENGINE/ARCHETYPES','COMMON/ARCHETYPES']:(data/name).mkdir(parents=True,exist_ok=True)
result=subprocess.run([str(bin/'Compiler_r.exe'),'/Input','ASSETS:/Effects/PaintSides.PFX.XML','/Platform','PC','/ProjectPath',str(job)],cwd=bin,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
(job/'build.log').write_text(result.stdout)
effect=data/'WIN32/EFFECTS/PAINTSIDES.PFX'
if result.returncode or 'ERROR:' in result.stdout or not effect.is_file():raise RuntimeError('Shader compilation failed; inspect '+str(job/'build.log'))
print(effect)
