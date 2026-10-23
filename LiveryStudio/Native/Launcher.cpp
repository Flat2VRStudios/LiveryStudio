#include <windows.h>
#include <string>
#include <vector>

namespace {
int Error(const std::wstring& message, DWORD code) {
    MessageBoxW(nullptr, message.c_str(), L"Livery Studio", MB_OK | MB_ICONERROR);
    return static_cast<int>(code);
}
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR arguments, int) {
    // Resolve beside this executable; Explorer and shortcuts can choose any cwd.
    std::vector<wchar_t> path(32768);
    const DWORD size = GetModuleFileNameW(nullptr, path.data(), static_cast<DWORD>(path.size()));
    if (size == 0 || size >= path.size())
        return Error(L"Couldn't locate Livery Studio. Extract the complete ZIP and try again.", ERROR_BAD_PATHNAME);
    const std::wstring module(path.data(), size);
    const auto separator = module.find_last_of(L"\\/");
    if (separator == std::wstring::npos)
        return Error(L"Couldn't locate Livery Studio's folder.", ERROR_BAD_PATHNAME);
    const std::wstring folder = module.substr(0, separator + 1) + L"App";
    const std::wstring executable = folder + L"\\LiveryStudio.exe";
    const DWORD attributes = GetFileAttributesW(executable.c_str());
    if (attributes == INVALID_FILE_ATTRIBUTES || (attributes & FILE_ATTRIBUTE_DIRECTORY))
        return Error(L"The App folder is missing or incomplete.\n\nExtract the complete ZIP and keep the App folder beside LiveryStudio.exe.", ERROR_FILE_NOT_FOUND);

    std::wstring command = L"\"" + executable + L"\"";
    if (arguments && *arguments) command += L" " + std::wstring(arguments);
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE, 0,
                        nullptr, folder.c_str(), &startup, &process)) {
        const DWORD code = GetLastError();
        return Error(L"Couldn't start Livery Studio (Windows error " + std::to_wstring(code) +
                     L").\n\nExtract the complete ZIP and keep the App folder beside LiveryStudio.exe.", code);
    }
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return 0;
}
