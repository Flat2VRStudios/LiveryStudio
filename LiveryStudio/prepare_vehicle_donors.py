"""Prepare the bundled donor sources/recipes; never writes into game source."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import struct
import subprocess
import xml.etree.ElementTree as ET
import zipfile


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def role(material):
    name=material.replace('\\','/').upper()
    return 'paint' if name.endswith('/LOAD.PMA.XML') else 'interior' if 'INTERIOR' in name else 'glass' if 'GLASS' in name else 'optics' if 'OPTICS' in name else 'other'


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--game-source',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--only',type=int)
    args=parser.parse_args();game_source=args.game_source.resolve();output=args.output.resolve()
    if output.is_relative_to(game_source):raise ValueError('Donor output must be outside game source')
    output.mkdir(parents=True,exist_ok=True)
    studio=Path(__file__).resolve().parent
    cars=json.loads((studio/'Core/Templates/catalog.json').read_text())
    config=(game_source/'Flatout/Data/Common/Tuning/Factories/Cars.cfg').read_text()
    mesh_catalog=json.loads((studio/'artifacts/models/catalog.json').read_text())
    catalog=[]
    with zipfile.ZipFile(game_source/'Flatout/Data1/Assets/GRAPH/Cars.zip') as archive:
        names={name.replace('\\','/').upper():name for name in archive.namelist()}
        for car in cars:
            if args.only is not None and car['Index']!=args.only:continue
            base=car['Car'].replace('_EVO','').replace('_G850','').replace('_V8','')
            engine=car['Class']+'_'+('IceCreamVan' if base=='ICECREAM_VAN' else base.title())
            block=re.search(r'^\t'+re.escape(engine)+r'\s*=\s*\{(.*?)^\t\}',config,re.M|re.S).group(1)
            fields=dict(re.findall(r'(\w+)\s*=\s*"([^"]+)"',block))
            resource=fields['BaseModel'].replace('\\','/')
            psi_path=resource.removeprefix('DATA:/').removeprefix('GRAPH/')+'.XML'
            folder=output/f"{car['Index']:02}";folder.mkdir(exist_ok=True)
            def extract(member,destination):
                actual=names[member.replace('\\','/').upper()]
                target=folder/destination;target.write_bytes(archive.read(actual));return target
            psi=extract(psi_path,'Body.PSI.XML')
            scene=ET.parse(psi).find('.//scene_instance').get('scene').replace('\\','/')
            p3d_path=re.sub(r'^\w+:/GRAPH/','',scene,flags=re.I)
            p3d=extract(p3d_path,'Body.P3D.XML')
            geometry=ET.parse(p3d).find('.//p3d').get('path').replace('\\','/')
            fbx=extract(re.sub(r'^\w+:/GRAPH/','',geometry,flags=re.I),'Body.FBX')
            bindings=[]
            for bind in ET.parse(psi).findall('.//bind'):
                a=bind.attrib
                if a.get('material'):
                    part_role=role(a['material'])
                    # Buick's cabin shell shares the paint shader. Preserve
                    # that geometry with the interior, as well as material-
                    # identified door trim and the separate steering wheel.
                    base_name=re.sub(r'_LOD\d+$','',a['mesh'],flags=re.I).upper()
                    if base_name in ('INTERIOR','STEERINGWHEEL'):
                        part_role='interiorpaint' if part_role=='paint' else 'interior'
                    bindings.append({'Node':a['mesh'],'Section':int(a.get('geometry',0)),'Role':part_role,'Resource':a['material']})
            (folder/'bindings.tsv').write_text(''.join(f"{b['Node']}\t{b['Section']}\t{b['Role']}\t{b['Resource']}\n" for b in bindings),encoding='utf-8')
            prefab=re.sub('^ARCHETYPES:/','Common/Archetypes/',fields['ArchetypeName'],flags=re.I).replace('\\','/')
            original_prefab=game_source/'Flatout/Data'/prefab
            doc=ET.parse(original_prefab)
            breakable=next(p.get('value') for c in doc.findall('.//component') if c.get('class')=='Rc2VehicleBreakables' for p in c.findall('./props/prop') if p.get('name')=='Breakables File')
            breakable=re.sub('^DATA:/','Common/',breakable,flags=re.I).replace('\\','/')
            shutil.copy2(original_prefab,folder/'Body.PPF')
            shutil.copy2(game_source/'Flatout/Data'/breakable,folder/'Breakables.cfg')
            bounds=mesh_catalog[car['Index']]['Bounds'];center=[(bounds[0][i]+bounds[1][i])/2 for i in range(3)]
            record={'Index':car['Index'],'Name':car['Name'],'Class':car['Class'],'CarId':car['Car'],
                    'BodyResource':resource,'PrefabPath':prefab,'BreakablesPath':breakable,
                    'FrontWheels':fields['FrontWheels'],'RearWheels':fields['RearWheels'],
                    'PreviewCenter':center,'Bindings':bindings,
                    'Files':{p.name:digest(p) for p in folder.iterdir() if p.is_file() and p.name!='donor.json'}}
            (folder/'donor.json').write_text(json.dumps(record,indent=2),encoding='utf-8')
            catalog.append(record)
            print(car['Index'],car['Name'],len(bindings),'material sections',flush=True)
    (output/'catalog.json').write_text(json.dumps(catalog,indent=2),encoding='utf-8')


if __name__=='__main__':main()
