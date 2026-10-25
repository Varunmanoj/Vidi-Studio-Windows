// Windows 11 modern File Explorer context-menu command for Vidi Studio.
//
// The packaged app registers this DLL as a COM surrogate server and associates
// CLSID_DescribeWithVidiStudio with the "Describe with Vidi Studio" verb through
// the desktop4/desktop5 fileExplorerContextMenus manifest extension. File
// Explorer loads the command in the shell surrogate; Invoke activates the
// packaged app with --video and the selected file path.

#include <windows.h>
#include <appmodel.h>
#include <shellapi.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <shobjidl.h>

#include <new>
#include <string>
#include <vector>

namespace
{
constexpr CLSID CLSID_DescribeWithVidiStudio =
    {0x8e2c4b1a, 0x7d4f, 0x4a2b, {0x9c, 0x3d, 0x6f, 0x1e, 0x5a, 0x8b, 0x0c, 0x42}};

constexpr wchar_t kMenuTitle[] = L"Describe with Vidi Studio";
constexpr wchar_t kAppUserModelIdSuffix[] = L"!App";
constexpr wchar_t kFallbackExecutable[] = L"Vidi Studio Windows.exe";
constexpr int kAppIconResourceId = 101;

HMODULE g_module = nullptr;
LONG g_objectCount = 0;
LONG g_lockCount = 0;

std::wstring ModulePath()
{
    std::vector<wchar_t> buffer(32768);
    const DWORD length = GetModuleFileNameW(g_module, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length == 0 || length >= buffer.size())
        return std::wstring();
    return std::wstring(buffer.data(), length);
}

std::wstring ModuleDirectory()
{
    const std::wstring path = ModulePath();
    const auto separator = path.find_last_of(L"\\/");
    return separator == std::wstring::npos ? std::wstring() : path.substr(0, separator);
}

std::wstring QuoteArgument(const std::wstring& value)
{
    std::wstring quoted;
    quoted.reserve(value.size() + 2);
    quoted.push_back(L'"');
    for (const wchar_t character : value)
    {
        if (character == L'"')
            quoted.push_back(L'\\');
        quoted.push_back(character);
    }
    quoted.push_back(L'"');
    return quoted;
}

bool IsVideoFile(const std::wstring& path)
{
    const auto separator = path.find_last_of(L'.');
    if (separator == std::wstring::npos)
        return false;
    const std::wstring extension = path.substr(separator);
    for (const wchar_t* candidate : {L".mp4", L".mov", L".m4v", L".mkv", L".avi", L".webm", L".wmv"})
    {
        if (CompareStringOrdinal(extension.c_str(), -1, candidate, -1, TRUE) == CSTR_EQUAL)
            return true;
    }
    return false;
}

HRESULT LaunchPackagedApp(const std::wstring& arguments)
{
    wchar_t familyName[PACKAGE_FAMILY_NAME_MAX_LENGTH + 1] = {};
    UINT32 familyNameLength = static_cast<UINT32>(ARRAYSIZE(familyName));
    if (GetCurrentPackageFamilyName(&familyNameLength, familyName) == ERROR_SUCCESS)
    {
        const std::wstring appUserModelId = std::wstring(familyName) + kAppUserModelIdSuffix;
        IApplicationActivationManager* manager = nullptr;
        HRESULT result = CoCreateInstance(
            CLSID_ApplicationActivationManager,
            nullptr,
            CLSCTX_LOCAL_SERVER,
            IID_PPV_ARGS(&manager));
        if (SUCCEEDED(result) && manager != nullptr)
        {
            DWORD processId = 0;
            result = manager->ActivateApplication(
                appUserModelId.c_str(),
                arguments.c_str(),
                AO_NONE,
                &processId);
            manager->Release();
            if (SUCCEEDED(result))
                return result;
        }
    }

    // Unpackaged or package-identity-unavailable fallback. The executable sits
    // beside this DLL in the package output.
    const std::wstring directory = ModuleDirectory();
    if (directory.empty())
        return E_FAIL;
    const std::wstring executable = directory + L"\\" + kFallbackExecutable;
    const HINSTANCE launched = ShellExecuteW(
        nullptr,
        L"open",
        executable.c_str(),
        arguments.c_str(),
        directory.c_str(),
        SW_SHOWNORMAL);
    if (reinterpret_cast<INT_PTR>(launched) > 32)
        return S_OK;
    const DWORD error = GetLastError();
    return error != ERROR_SUCCESS ? HRESULT_FROM_WIN32(error) : E_FAIL;
}

HRESULT InvokeForSelection(IShellItemArray* items)
{
    if (items == nullptr)
        return E_INVALIDARG;

    DWORD count = 0;
    if (FAILED(items->GetCount(&count)) || count == 0)
        return E_INVALIDARG;

    IShellItem* item = nullptr;
    if (FAILED(items->GetItemAt(0, &item)) || item == nullptr)
        return E_FAIL;

    PWSTR rawPath = nullptr;
    const HRESULT displayName = item->GetDisplayName(SIGDN_FILESYSPATH, &rawPath);
    item->Release();
    if (FAILED(displayName) || rawPath == nullptr)
        return E_FAIL;

    const std::wstring path(rawPath);
    CoTaskMemFree(rawPath);
    if (!IsVideoFile(path))
        return E_INVALIDARG;

    return LaunchPackagedApp(L"--video " + QuoteArgument(path));
}

class DescribeWithVidiStudioCommand final : public IExplorerCommand
{
public:
    DescribeWithVidiStudioCommand()
    {
        InterlockedIncrement(&g_objectCount);
    }

    ~DescribeWithVidiStudioCommand()
    {
        InterlockedDecrement(&g_objectCount);
    }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** result) override
    {
        if (result == nullptr)
            return E_POINTER;
        *result = nullptr;
        if (riid == IID_IUnknown || riid == IID_IExplorerCommand)
        {
            *result = static_cast<IExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override
    {
        return static_cast<ULONG>(InterlockedIncrement(&referenceCount_));
    }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        const LONG count = InterlockedDecrement(&referenceCount_);
        if (count == 0)
            delete this;
        return static_cast<ULONG>(count);
    }

    IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* title) override
    {
        if (title == nullptr)
            return E_POINTER;
        return SHStrDupW(kMenuTitle, title);
    }

    IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override
    {
        if (icon == nullptr)
            return E_POINTER;
        const std::wstring module = ModulePath();
        if (module.empty())
            return E_FAIL;
        const std::wstring resource = module + L",-" + std::to_wstring(kAppIconResourceId);
        return SHStrDupW(resource.c_str(), icon);
    }

    IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* tooltip) override
    {
        if (tooltip != nullptr)
            *tooltip = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetCanonicalName(GUID* canonicalName) override
    {
        if (canonicalName != nullptr)
            *canonicalName = GUID_NULL;
        return S_OK;
    }

    IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
    {
        if (state == nullptr)
            return E_POINTER;
        *state = ECS_HIDDEN;

        if (items == nullptr)
            return S_OK;

        DWORD count = 0;
        if (FAILED(items->GetCount(&count)) || count != 1)
            return S_OK;

        IShellItem* item = nullptr;
        if (FAILED(items->GetItemAt(0, &item)) || item == nullptr)
            return S_OK;

        PWSTR rawPath = nullptr;
        const HRESULT displayName = item->GetDisplayName(SIGDN_FILESYSPATH, &rawPath);
        item->Release();
        if (FAILED(displayName) || rawPath == nullptr)
            return S_OK;

        const std::wstring path(rawPath);
        CoTaskMemFree(rawPath);
        if (IsVideoFile(path))
            *state = ECS_ENABLED;
        return S_OK;
    }

    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
    {
        return InvokeForSelection(items);
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
    {
        if (flags == nullptr)
            return E_POINTER;
        *flags = ECF_DEFAULT;
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override
    {
        if (commands != nullptr)
            *commands = nullptr;
        return E_NOTIMPL;
    }

private:
    LONG referenceCount_ = 1;
};

class DescribeWithVidiStudioFactory final : public IClassFactory
{
public:
    DescribeWithVidiStudioFactory()
    {
        InterlockedIncrement(&g_objectCount);
    }

    ~DescribeWithVidiStudioFactory()
    {
        InterlockedDecrement(&g_objectCount);
    }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** result) override
    {
        if (result == nullptr)
            return E_POINTER;
        *result = nullptr;
        if (riid == IID_IUnknown || riid == IID_IClassFactory)
        {
            *result = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override
    {
        return static_cast<ULONG>(InterlockedIncrement(&referenceCount_));
    }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        const LONG count = InterlockedDecrement(&referenceCount_);
        if (count == 0)
            delete this;
        return static_cast<ULONG>(count);
    }

    IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** result) override
    {
        if (result == nullptr)
            return E_POINTER;
        *result = nullptr;
        if (outer != nullptr)
            return CLASS_E_NOAGGREGATION;

        auto* command = new (std::nothrow) DescribeWithVidiStudioCommand();
        if (command == nullptr)
            return E_OUTOFMEMORY;
        const HRESULT created = command->QueryInterface(riid, result);
        command->Release();
        return created;
    }

    IFACEMETHODIMP LockServer(BOOL lock) override
    {
        if (lock)
            InterlockedIncrement(&g_lockCount);
        else
            InterlockedDecrement(&g_lockCount);
        return S_OK;
    }

private:
    LONG referenceCount_ = 1;
};
} // namespace

BOOL WINAPI DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}

STDAPI DllGetClassObject(
    REFCLSID classId,
    REFIID interfaceId,
    LPVOID FAR* result)
{
    if (result == nullptr)
        return E_POINTER;
    *result = nullptr;
    if (!InlineIsEqualGUID(classId, CLSID_DescribeWithVidiStudio))
        return CLASS_E_CLASSNOTAVAILABLE;

    auto* factory = new (std::nothrow) DescribeWithVidiStudioFactory();
    if (factory == nullptr)
        return E_OUTOFMEMORY;
    const HRESULT created = factory->QueryInterface(interfaceId, result);
    factory->Release();
    return created;
}

STDAPI DllCanUnloadNow(void)
{
    return (g_objectCount == 0 && g_lockCount == 0) ? S_OK : S_FALSE;
}
