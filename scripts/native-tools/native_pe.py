"""Minimal read-only PE import inventory for native tool packaging."""
from pathlib import Path
import struct


def inspect_pe(path):
    data=Path(path).read_bytes()
    if data[:2]!=b'MZ':raise ValueError('Not a PE file')
    pe=struct.unpack_from('<I',data,0x3c)[0]
    if data[pe:pe+4]!=b'PE\0\0':raise ValueError('Invalid PE signature')
    machine,sections=struct.unpack_from('<HH',data,pe+4)
    size=struct.unpack_from('<H',data,pe+20)[0];optional=pe+24
    magic=struct.unpack_from('<H',data,optional)[0]
    directories=optional+(112 if magic==0x20b else 96)
    table=optional+size
    chunks=[struct.unpack_from('<8sIIII',data,table+i*40) for i in range(sections)]
    def offset(rva):
        for _,virtual_size,virtual,raw_size,raw in chunks:
            if virtual<=rva<virtual+max(virtual_size,raw_size):return raw+rva-virtual
        if rva<table:return rva
        raise ValueError('Invalid PE RVA')
    def string(rva):
        start=offset(rva);return data[start:data.index(b'\0',start)].decode('ascii')
    imports=[]
    rva=struct.unpack_from('<I',data,directories+8)[0]
    if rva:
        pos=offset(rva)
        while any(struct.unpack_from('<5I',data,pos)):
            imports.append(string(struct.unpack_from('<I',data,pos+12)[0]));pos+=20
    delay=struct.unpack_from('<I',data,directories+13*8)[0]
    if delay:
        pos=offset(delay)
        while any(struct.unpack_from('<8I',data,pos)):
            flags,name=struct.unpack_from('<2I',data,pos)
            if flags&1:imports.append(string(name))
            else:raise ValueError('Unsupported VA delay-import table')
            pos+=32
    return machine,sorted(set(imports),key=str.lower)
