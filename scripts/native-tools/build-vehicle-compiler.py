"""Generate an isolated x64 compiler project against the existing game source libs.

No source project, post-build deployment destination, or game source binary is changed.
This is a toolchain spike, not the end-user build path.
"""
import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--game-source',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--project',choices=['Compiler','Importer','Writer','WriterPhysX','WriterWin32'],default='Compiler')
    parser.add_argument('--stable-vertex-keys',action='store_true')
    parser.add_argument('--geometry-only',action='store_true')
    parser.add_argument('--entry-source',type=Path)
    args=parser.parse_args()
    base=args.game_source.resolve()/'PlayAll/Code/Projects 2022'
    args.output=args.output.resolve()
    if args.output.is_relative_to(args.game_source.resolve()):raise ValueError('Build output must be outside game source')
    if args.stable_vertex_keys and args.project!='Importer':raise ValueError('Stable vertex keys apply to Importer')
    if args.entry_source and args.geometry_only:raise ValueError('Choose a probe entry or geometry compiler')
    inputs={}
    def record(path):
        path=path.resolve()
        inputs[str(path)]=hashlib.sha256(path.read_bytes()).hexdigest().upper()
    record(base/(args.project+'.vcxproj'))
    ns={'m':'http://schemas.microsoft.com/developer/msbuild/2003'}
    tag='{'+ns['m']+'}'
    ET.register_namespace('',ns['m'])
    tree=ET.parse(base/(args.project+'.vcxproj'));root=tree.getroot()
    if args.entry_source:
        record(args.entry_source)
        if args.project!='Compiler':raise ValueError('Custom entry applies to Compiler')
        for group in root.findall('m:ItemGroup',ns):
            for item in list(group.findall('m:ClCompile',ns)):
                if item.get('Include'):group.remove(item)
        group=ET.SubElement(root,tag+'ItemGroup')
        ET.SubElement(group,tag+'ClCompile',{'Include':str(args.entry_source.resolve())})
    configuration="'$(Configuration)|$(Platform)'=='Release|x64'"
    libraries=[]
    args.output.mkdir(parents=True,exist_ok=True)
    if args.geometry_only:
        if args.project!='Compiler':raise ValueError('Geometry-only entry applies to Compiler')
        source=base/'../Tools/PlayAll/Tools/ContentPipeline/Compiler/CommandLine/ContentCompiler.cpp'
        record(source)
        code=source.read_text(encoding='cp1252')
        if code.count('\tRegisterClassExA( &wc );')!=1:raise ValueError('Compiler initialization source changed')
        start=code.index('\tRegisterClassExA( &wc );')
        end=code.index('\tPA_TEST_OK( PlayAll::Init() );',start)
        code=code[:start]+'\t// Geometry compilation uses explicit LODs and does not create a texture compiler device.\n'+code[end:]
        target=args.output/'ContentCompiler.cpp';target.write_text(code,encoding='cp1252')
        for item in root.findall('.//m:ClCompile',ns):
            if item.get('Include','').endswith('ContentCompiler.cpp'):item.set('Include',str(target.resolve()))
    if args.stable_vertex_keys:
        # A polymorphic Vertex's object representation includes a module-local
        # vtable and padding. Comparing it wholesale makes equal paint vertices
        # unstable map keys across the importer/writer DLL boundary.
        relative='PlayAll/Tools/ContentPipeline/Content/Vertex.h'
        source=base/'../Tools'/relative
        record(source)
        header=source.read_text(encoding='cp1252')
        old='return memcmp( this, &Src, sizeof(Src) ) < 0;'
        if header.count(old)!=1:raise ValueError('Vertex comparator source changed')
        fields=['m_uUniquePositionIndex']
        fields += [f'{vector}.{axis}' for vector,axes in [('m_oPos','xyz'),('m_oNorm','xyz'),('m_oTan','xyz'),('m_oBin','xyz'),('m_oUV0','xy'),('m_oUV1','xy'),('m_oColor','xyzw')] for axis in axes]
        fields += [f'{array}[{index}]' for array in ['m_Bones','m_Weights'] for index in range(4)]
        comparison='int order;\n'+''.join(f'\t\t\t\t\tif ((order=memcmp(&{field}, &Src.{field}, sizeof({field}))) != 0) return order < 0;\n' for field in fields)+'\t\t\t\t\treturn false;'
        target=args.output/'include'/relative;target.parent.mkdir(parents=True,exist_ok=True)
        target.write_text(header.replace(old,comparison),encoding='cp1252')
        remapper=base/'../Tools/PlayAll/Tools/ContentPipeline/Content/MeshRemapper.cpp'
        record(remapper)
        text=remapper.read_text(encoding='cp1252')
        if text.count('\t\t\t\tmap<Content::SceneContent::Vertex, uint32>::iterator it =')!=1:raise ValueError('Mesh remapper source changed')
        start=text.index('\t\t\t\tmap<Content::SceneContent::Vertex, uint32>::iterator it =')
        end=text.index('\n\t\t\t}',start)
        replacement='\t\t\t\tauto inserted = m_VertexToIndex.insert(std::make_pair(Vertex, (uint32)m_VertexToIndex.size()));\n\t\t\t\tif (inserted.second) m_IndexToVertex[inserted.first->second] = Vertex;\n\t\t\t\treturn inserted.first->second;'
        (args.output/'MeshRemapper.cpp').write_text(text[:start]+replacement+text[end:],encoding='cp1252')
        group=ET.SubElement(root,tag+'ItemGroup')
        item=ET.SubElement(group,tag+'ClCompile',{'Include':str((args.output/'MeshRemapper.cpp').resolve())})
        ET.SubElement(item,tag+'PrecompiledHeader').text='NotUsing'
    for group in root.findall('m:ItemGroup',ns):
        for ref in list(group.findall('m:ProjectReference',ns)):
            if ref.get('Condition')==configuration:
                record(base/ref.get('Include'))
                source=ET.parse(base/ref.get('Include')).getroot()
                target=next(g.find('m:TargetName',ns).text for g in source.findall('m:PropertyGroup',ns)
                            if g.get('Condition')==configuration and g.find('m:TargetName',ns) is not None)
                library=(base/'../../Libs/vs2022/x64'/(target+'.lib')).resolve()
                if not library.is_file():raise FileNotFoundError(library)
                libraries.append(library)
            group.remove(ref)
        for item in group:
            if item.tag in [tag+'ClCompile',tag+'ClInclude',tag+'ResourceCompile'] and item.get('Include'):
                source=(base/item.get('Include')).resolve()
                if source.is_file():record(source)
                item.set('Include',str(source))
    for group in root.findall('m:ItemDefinitionGroup',ns):
        for event in list(group.findall('m:PostBuildEvent',ns))+list(group.findall('m:PreBuildEvent',ns)):
            group.remove(event)
        if group.get('Condition')!=configuration:continue
        for dirs in group.findall('.//m:AdditionalIncludeDirectories',ns)+group.findall('.//m:AdditionalLibraryDirectories',ns):
            dirs.text=';'.join(str((base/p).resolve()) if p and '$(' not in p and '%(' not in p else p for p in dirs.text.split(';'))
            if args.stable_vertex_keys and dirs.tag==tag+'AdditionalIncludeDirectories':dirs.text=str((args.output/'include').resolve())+';'+dirs.text
        deps=group.find('m:Link/m:AdditionalDependencies',ns)
        # game source's legacy importer project can still name the x86 FSBank import
        # library. The isolated worker and its private dependencies are x64.
        deps.text=(';'.join(map(str,libraries))+';'+deps.text).replace('fsbank_vc.lib','fsbank64_vc.lib')
        if args.project=='WriterWin32':
            api=(base/'../../Libs/vs2022/x64/CompilerAPI_r.lib').resolve()
            record(api);deps.text=str(api)+';'+deps.text
        library=group.find('m:Link/m:ImportLibrary',ns)
        if library is not None:library.text='$(OutDir)$(TargetName).lib'
    for element in root.findall('.//m:OutDir',ns):element.text=str(args.output/'bin')+'\\'
    for element in root.findall('.//m:IntDir',ns):element.text=str(args.output/'obj')+'\\'
    # This generated project only supports the recorded ABI/configuration.
    for group in root.findall('m:ItemGroup',ns):
        for item in list(group.findall('m:ProjectConfiguration',ns)):
            if item.get('Include')!='Release|x64':group.remove(item)
    check=ET.SubElement(root,tag+'Target',{'Name':'ValidateVehicleConfiguration','BeforeTargets':'PrepareForBuild'})
    ET.SubElement(check,tag+'Error',{'Condition':"'$(Configuration)|$(Platform)'!='Release|x64'",'Text':'Vehicle worker projects support Release|x64 only.'})
    tree.write(args.output/('Vehicle'+args.project+'.vcxproj'),encoding='utf-8',xml_declaration=True)
    (args.output/'input-libraries.json').write_text(json.dumps({str(p):hashlib.sha256(p.read_bytes()).hexdigest().upper() for p in libraries},indent=2),encoding='utf-8')
    (args.output/'build-inputs.json').write_text(json.dumps({
        'project':args.project,'configuration':'Release|x64',
        'stableVertexKeys':args.stable_vertex_keys,'geometryOnly':args.geometry_only,
        'sourceInputs':inputs,'generatorSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest().upper(),
        'scope':'Generated overlay/project only; relies on the recorded existing game source libraries',
    },indent=2),encoding='utf-8')
    print(args.output/('Vehicle'+args.project+'.vcxproj'))


if __name__=='__main__':main()
