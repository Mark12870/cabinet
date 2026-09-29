typedef unsigned short WCHAR;
typedef void *HANDLE;
typedef void *HKEY;
typedef unsigned long DWORD;
typedef int BOOL;
typedef unsigned int UINT;

#define WINAPI __stdcall
#define ERROR_SUCCESS 0
#define ERROR_INSTALL_FAILURE 1603
#define CP_ACP 0
#define GENERIC_READ 0x80000000
#define OPEN_EXISTING 3
#define INVALID_HANDLE_VALUE ((HANDLE)(long)-1)
#define HKEY_LOCAL_MACHINE ((HKEY)(unsigned long)0x80000002)
#define KEY_SET_VALUE 0x0002
#define KEY_WOW64_64KEY 0x0100
#define REG_SZ 1
#define CREATE_NO_WINDOW 0x08000000
#define MAX_PATH 260

typedef struct {
    DWORD cb;
    WCHAR *reserved, *desktop, *title;
    DWORD x, y, width, height, x_chars, y_chars, fill, flags;
    unsigned short show_window, reserved2;
    void *reserved3;
    HANDLE input, output, error;
} STARTUPINFOW;

typedef struct {
    HANDLE process, thread;
    DWORD process_id, thread_id;
} PROCESS_INFORMATION;

typedef UINT(WINAPI *InstallA)(const char *, const char *);
typedef UINT(WINAPI *InstallW)(const WCHAR *, const WCHAR *);
typedef char *(__cdecl *UnixName)(const WCHAR *);

__declspec(dllimport) HANDLE WINAPI LoadLibraryW(const WCHAR *);
__declspec(dllimport) void *WINAPI GetProcAddress(HANDLE, const char *);
__declspec(dllimport) HANDLE WINAPI GetModuleHandleW(const WCHAR *);
__declspec(dllimport) BOOL WINAPI CreateProcessW(const WCHAR *, WCHAR *, void *, void *, BOOL, DWORD, void *,
                                                  const WCHAR *, STARTUPINFOW *, PROCESS_INFORMATION *);
__declspec(dllimport) BOOL WINAPI CloseHandle(HANDLE);
__declspec(dllimport) HANDLE WINAPI CreateFileW(const WCHAR *, DWORD, DWORD, void *, DWORD, DWORD, HANDLE);
__declspec(dllimport) BOOL WINAPI ReadFile(HANDLE, void *, DWORD, DWORD *, void *);
__declspec(dllimport) BOOL WINAPI DeleteFileW(const WCHAR *);
__declspec(dllimport) void WINAPI Sleep(DWORD);
__declspec(dllimport) int WINAPI MultiByteToWideChar(UINT, DWORD, const char *, int, WCHAR *, int);
__declspec(dllimport) HANDLE WINAPI GetProcessHeap(void);
__declspec(dllimport) void *WINAPI HeapAlloc(HANDLE, DWORD, unsigned long);
__declspec(dllimport) BOOL WINAPI HeapFree(HANDLE, DWORD, void *);
__declspec(dllimport) long WINAPI RegCreateKeyExW(HKEY, const WCHAR *, DWORD, WCHAR *, DWORD, DWORD, void *,
                                                  HKEY *, DWORD *);
__declspec(dllimport) long WINAPI RegSetValueExW(HKEY, const WCHAR *, DWORD, DWORD, const void *, DWORD);
__declspec(dllimport) long WINAPI RegCloseKey(HKEY);

static const WCHAR Diverted[] = L"Kontakt 8 Setup PC.msi";
static const WCHAR Result[] = L"C:\\windows\\temp\\cabinet-msi.result";
static const WCHAR Key[] = L"SOFTWARE\\Native Instruments\\Kontakt 8";
static const char Hook[] = "/app/share/cabinet/library/native-instruments/kontakt-8.sh";
static const DWORD Patience = 600;

void *memset(void *to, int value, unsigned long size)
{
    unsigned char *byte = to;

    while (size--)
        *byte++ = (unsigned char)value;

    return to;
}

static HANDLE wine_msi(void)
{
    static HANDLE module;

    if (!module)
        module = LoadLibraryW(L"msi_wine.dll");

    return module;
}

static int length(const WCHAR *text)
{
    int count = 0;

    while (text && text[count])
        count++;

    return count;
}

static int lower(WCHAR c)
{
    return c >= 'A' && c <= 'Z' ? c + 32 : c;
}

static BOOL diverted(const WCHAR *package)
{
    int have = length(package), want = length(Diverted);

    if (have < want || (have > want && package[have - want - 1] != '\\' && package[have - want - 1] != '/'))
        return 0;

    for (int i = 0; i < want; i++)
        if (lower(package[have - want + i]) != lower(Diverted[i]))
            return 0;

    return 1;
}

static void append(WCHAR *to, int *at, const WCHAR *text)
{
    for (int i = 0; text[i]; i++)
        to[(*at)++] = text[i];
    to[*at] = 0;
}

static void append_narrow(WCHAR *to, int *at, const char *text)
{
    for (int i = 0; text[i]; i++)
        to[(*at)++] = (unsigned char)text[i];
    to[*at] = 0;
}

static BOOL hook(void)
{
    UnixName unix_name = (UnixName)GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "wine_get_unix_file_name");
    char *drive = unix_name ? unix_name(L"C:\\") : 0;
    WCHAR command[4 * MAX_PATH];
    int at = 0;
    STARTUPINFOW startup;
    PROCESS_INFORMATION started;

    if (!drive)
        return 0;

    append(command, &at, L"C:\\windows\\system32\\start.exe /unix /bin/sh ");
    append_narrow(command, &at, Hook);
    append(command, &at, L" --installing ");
    append_narrow(command, &at, drive);
    HeapFree(GetProcessHeap(), 0, drive);

    DeleteFileW(Result);
    memset(&startup, 0, sizeof(startup));
    startup.cb = sizeof(startup);

    if (!CreateProcessW(0, command, 0, 0, 0, CREATE_NO_WINDOW, 0, 0, &startup, &started))
        return 0;

    CloseHandle(started.thread);
    CloseHandle(started.process);
    return 1;
}

static int awaited(char *said, DWORD size)
{
    for (DWORD waited = 0; waited < Patience; waited++) {
        HANDLE file = CreateFileW(Result, GENERIC_READ, 0, 0, OPEN_EXISTING, 0, 0);

        if (file != INVALID_HANDLE_VALUE) {
            DWORD read = 0;
            ReadFile(file, said, size - 1, &read, 0);
            CloseHandle(file);
            said[read] = 0;
            DeleteFileW(Result);
            return 1;
        }

        Sleep(1000);
    }

    return 0;
}

static void recorded(const char *version)
{
    WCHAR wide[64];
    HKEY key;
    int at = 0;

    append_narrow(wide, &at, version);

    if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, Key, 0, 0, 0, KEY_SET_VALUE | KEY_WOW64_64KEY, 0, &key, 0))
        return;

    RegSetValueExW(key, L"InstallVST64Dir", 0, REG_SZ, L"C:\\Program Files\\Common Files\\VST3",
                   (length(L"C:\\Program Files\\Common Files\\VST3") + 1) * sizeof(WCHAR));
    RegSetValueExW(key, L"Version", 0, REG_SZ, wide, (at + 1) * sizeof(WCHAR));
    RegCloseKey(key);
}

static UINT installed(void)
{
    char said[64];
    char *version = said + 3;

    if (!hook() || !awaited(said, sizeof(said)) || said[0] != 'o' || said[1] != 'k' || said[2] != ' ')
        return ERROR_INSTALL_FAILURE;

    for (char *end = version; *end; end++)
        if (*end == '\n' || *end == '\r')
            *end = 0;

    recorded(version);
    return ERROR_SUCCESS;
}

UINT WINAPI MsiInstallProductW(const WCHAR *package, const WCHAR *command)
{
    InstallW forward;

    if (diverted(package))
        return installed();

    forward = (InstallW)GetProcAddress(wine_msi(), "MsiInstallProductW");
    return forward ? forward(package, command) : ERROR_INSTALL_FAILURE;
}

UINT WINAPI MsiInstallProductA(const char *package, const char *command)
{
    InstallA forward;
    WCHAR wide[MAX_PATH];

    if (package && MultiByteToWideChar(CP_ACP, 0, package, -1, wide, MAX_PATH) && diverted(wide))
        return installed();

    forward = (InstallA)GetProcAddress(wine_msi(), "MsiInstallProductA");
    return forward ? forward(package, command) : ERROR_INSTALL_FAILURE;
}

#define FORWARD(name) __attribute__((used)) void *name##_real;
#include "forwards.h"
#undef FORWARD

#define FORWARD(name) __asm__(".globl _" #name "_stub\n_" #name "_stub:\n    jmp *_" #name "_real\n");
#include "forwards.h"
#undef FORWARD

BOOL WINAPI DllMain(HANDLE module, DWORD reason, void *reserved)
{
    HANDLE wine;

    (void)module;
    (void)reserved;

    if (reason != 1)
        return 1;

    wine = wine_msi();

    if (!wine)
        return 0;

#define FORWARD(name) name##_real = GetProcAddress(wine, #name);
#include "forwards.h"
#undef FORWARD

    return 1;
}
