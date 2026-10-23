"""Read/update a staged DLL's manifest without loading executable code.

Used by the developer packaging step, never against the source installation.
The compiler's old Tootle dependency otherwise binds to an installed VC90 CRT
even when loose copies of its DLLs are present beside the compiler.
"""
import ctypes
from ctypes import wintypes
from pathlib import Path
import xml.etree.ElementTree as ET


NAMESPACE = 'urn:schemas-microsoft-com:asm.v1'
ASSEMBLY = '{' + NAMESPACE + '}'


def private_crt_manifest(data):
    """Retain native CRT file hashes, use an app-private identity."""
    root = ET.fromstring(data)
    identity = root.find(ASSEMBLY + 'assemblyIdentity')
    if identity is None or identity.get('name') != 'Microsoft.VC90.CRT':
        raise ValueError('Expected the original Microsoft.VC90.CRT manifest')
    if identity.get('processorArchitecture') != 'amd64':
        raise ValueError('The vehicle worker requires an amd64 CRT assembly')
    version = identity.get('version')
    if not version or not version.startswith('9.0.'):
        raise ValueError('Expected a VC90 assembly version')
    identity.attrib.pop('publicKeyToken', None)
    # Tootle is native C++; its assembly needs no optional managed C++ bridge.
    for file in list(root.findall(ASSEMBLY + 'file')):
        if file.get('name', '').lower() == 'msvcm90.dll':
            root.remove(file)
    ET.register_namespace('', NAMESPACE)
    return ET.tostring(root, encoding='utf-8', xml_declaration=True), version


def bind_private_crt(data, version):
    root = ET.fromstring(data)
    identities = root.findall('.//' + ASSEMBLY + 'dependentAssembly/' + ASSEMBLY + 'assemblyIdentity')
    matches = [i for i in identities if i.get('name') == 'Microsoft.VC90.CRT']
    if len(matches) != 1 or matches[0].get('processorArchitecture') != 'amd64':
        raise ValueError('Expected one amd64 VC90 dependency in the staged Tootle DLL')
    matches[0].set('version', version)
    matches[0].attrib.pop('publicKeyToken', None)
    ET.register_namespace('', NAMESPACE)
    return ET.tostring(root, encoding='utf-8', xml_declaration=True)


def rebind_staged_crt(path, version):
    """Update every language of RT_MANIFEST #2 in a copied, unsigned DLL."""
    path = Path(path).resolve()
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.LoadLibraryExW.argtypes = [wintypes.LPCWSTR, wintypes.HANDLE, wintypes.DWORD]
    kernel.LoadLibraryExW.restype = wintypes.HMODULE
    kernel.FreeLibrary.argtypes = [wintypes.HMODULE]
    kernel.FindResourceExW.argtypes = [wintypes.HMODULE, ctypes.c_void_p, ctypes.c_void_p, wintypes.WORD]
    kernel.FindResourceExW.restype = wintypes.HANDLE
    kernel.LoadResource.argtypes = [wintypes.HMODULE, wintypes.HANDLE]
    kernel.LoadResource.restype = wintypes.HANDLE
    kernel.LockResource.argtypes = [wintypes.HANDLE]
    kernel.LockResource.restype = ctypes.c_void_p
    kernel.SizeofResource.argtypes = [wintypes.HMODULE, wintypes.HANDLE]
    kernel.SizeofResource.restype = wintypes.DWORD
    callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HMODULE, ctypes.c_void_p,
                                      ctypes.c_void_p, wintypes.WORD, ctypes.c_ssize_t)
    kernel.EnumResourceLanguagesW.argtypes = [wintypes.HMODULE, ctypes.c_void_p, ctypes.c_void_p,
                                             callback_type, ctypes.c_ssize_t]
    kernel.EnumResourceLanguagesW.restype = wintypes.BOOL
    # DATAFILE | IMAGE_RESOURCE reads resources without DllMain or imports.
    handle = kernel.LoadLibraryExW(str(path), None, 0x22)
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    languages = []
    manifests = {}
    try:
        callback = callback_type(lambda _h, _t, _n, language, _p: languages.append(language) or True)
        if not kernel.EnumResourceLanguagesW(handle, 24, 2, callback, 0):
            raise ctypes.WinError(ctypes.get_last_error())
        for language in languages:
            resource = kernel.FindResourceExW(handle, 24, 2, language)
            loaded = kernel.LoadResource(handle, resource)
            address = kernel.LockResource(loaded)
            size = kernel.SizeofResource(handle, resource)
            if not address or not size:
                raise ValueError('Empty native manifest resource')
            manifests[language] = bind_private_crt(ctypes.string_at(address, size), version)
    finally:
        kernel.FreeLibrary(handle)

    kernel.BeginUpdateResourceW.argtypes = [wintypes.LPCWSTR, wintypes.BOOL]
    kernel.BeginUpdateResourceW.restype = wintypes.HANDLE
    kernel.UpdateResourceW.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p,
                                      wintypes.WORD, ctypes.c_void_p, wintypes.DWORD]
    kernel.UpdateResourceW.restype = wintypes.BOOL
    kernel.EndUpdateResourceW.argtypes = [wintypes.HANDLE, wintypes.BOOL]
    kernel.EndUpdateResourceW.restype = wintypes.BOOL
    update = kernel.BeginUpdateResourceW(str(path), False)
    if not update:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        for language, data in manifests.items():
            buffer = ctypes.create_string_buffer(data)
            if not kernel.UpdateResourceW(update, 24, 2, language, buffer, len(data)):
                raise ctypes.WinError(ctypes.get_last_error())
        if not kernel.EndUpdateResourceW(update, False):
            raise ctypes.WinError(ctypes.get_last_error())
        update = None
    finally:
        if update:
            kernel.EndUpdateResourceW(update, True)
    return sorted(manifests)
