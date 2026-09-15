#include <windows.h>
#include <ole2.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define FAULTED_ON_THE_FOREIGN_TARGET 0xdd
#define FAULTED_ELSEWHERE 0xde

static ULONG_PTR foreign_target;

static HRESULT STDMETHODCALLTYPE query(IDropTarget *self, REFIID iid, void **out)
{
    if (IsEqualIID(iid, &IID_IUnknown) || IsEqualIID(iid, &IID_IDropTarget))
    {
        *out = self;
        return S_OK;
    }

    *out = NULL;
    return E_NOINTERFACE;
}

static ULONG STDMETHODCALLTYPE reference(IDropTarget *self)
{
    return 1;
}

static HRESULT STDMETHODCALLTYPE enter(IDropTarget *self, IDataObject *data, DWORD keys, POINTL at, DWORD *effect)
{
    return S_OK;
}

static HRESULT STDMETHODCALLTYPE over(IDropTarget *self, DWORD keys, POINTL at, DWORD *effect)
{
    return S_OK;
}

static HRESULT STDMETHODCALLTYPE leave(IDropTarget *self)
{
    return S_OK;
}

static HRESULT STDMETHODCALLTYPE drop(IDropTarget *self, IDataObject *data, DWORD keys, POINTL at, DWORD *effect)
{
    return S_OK;
}

static IDropTargetVtbl methods = { query, reference, reference, enter, over, leave, drop };

static LONG WINAPI faulted(EXCEPTION_POINTERS *exception)
{
    const EXCEPTION_RECORD *record = exception->ExceptionRecord;
    const BOOL on_target = record->ExceptionCode == EXCEPTION_ACCESS_VIOLATION
                           && record->NumberParameters >= 2
                           && record->ExceptionInformation[1] == foreign_target;

    printf("fault %08lx reading %llx\n", (unsigned long)record->ExceptionCode,
           (unsigned long long)(record->NumberParameters >= 2 ? record->ExceptionInformation[1] : 0));
    ExitProcess(on_target ? FAULTED_ON_THE_FOREIGN_TARGET : FAULTED_ELSEWHERE);
    return EXCEPTION_EXECUTE_HANDLER;
}

static IDropTarget *unreachable_target(void)
{
    static const ULONG_PTR places[] = { 0x100000000000, 0x200000000000, 0x300000000000, 0x400000000000 };

    for (size_t i = 0; i < sizeof(places) / sizeof(places[0]); i++)
    {
        IDropTarget *target = VirtualAlloc((void *)places[i], sizeof(*target), MEM_RESERVE | MEM_COMMIT,
                                           PAGE_READWRITE);
        if (target)
        {
            target->lpVtbl = &methods;
            return target;
        }
    }

    return NULL;
}

static int revoke(const char *window, const char *target)
{
    foreign_target = (ULONG_PTR)_strtoui64(target, NULL, 16);
    SetUnhandledExceptionFilter(faulted);
    OleInitialize(NULL);

    HRESULT result = RevokeDragDrop((HWND)(ULONG_PTR)_strtoui64(window, NULL, 16));
    printf("revoke returned %08lx\n", (unsigned long)result);
    return 0;
}

static DWORD wait_pumping(HANDLE process)
{
    const ULONGLONG deadline = GetTickCount64() + 60000;

    for (;;)
    {
        const ULONGLONG now = GetTickCount64();
        if (now >= deadline)
        {
            return WAIT_TIMEOUT;
        }

        DWORD woke = MsgWaitForMultipleObjects(1, &process, FALSE, (DWORD)(deadline - now), QS_ALLINPUT);
        if (woke != WAIT_OBJECT_0 + 1)
        {
            return woke;
        }

        MSG message;
        while (PeekMessageA(&message, NULL, 0, 0, PM_REMOVE))
        {
            DispatchMessageA(&message);
        }
    }
}

static int hold(const char *self)
{
    OleInitialize(NULL);

    HWND window = CreateWindowA("STATIC", "drag-drop probe", 0, 0, 0, 0, 0, HWND_MESSAGE, NULL, NULL, NULL);
    IDropTarget *target = unreachable_target();
    if (!window || !target)
    {
        printf("probe could not set up a window and an unreachable drop target\n");
        return 1;
    }

    HRESULT registered = RegisterDragDrop(window, target);
    if (FAILED(registered))
    {
        printf("probe could not register drag-and-drop: %08lx\n", (unsigned long)registered);
        return 1;
    }

    char command[MAX_PATH + 96];
    snprintf(command, sizeof(command), "\"%s\" revoke %llx %llx", self, (unsigned long long)(ULONG_PTR)window,
             (unsigned long long)(ULONG_PTR)target);

    STARTUPINFOA startup = { .cb = sizeof(startup) };
    PROCESS_INFORMATION child;
    if (!CreateProcessA(NULL, command, NULL, NULL, TRUE, 0, NULL, NULL, &startup, &child))
    {
        printf("probe could not start the revoking process: %lu\n", GetLastError());
        return 1;
    }

    if (wait_pumping(child.hProcess) != WAIT_OBJECT_0)
    {
        TerminateProcess(child.hProcess, 1);
        printf("revoke hung\n");
        return 0;
    }

    DWORD code = 0;
    GetExitCodeProcess(child.hProcess, &code);
    if (code == FAULTED_ON_THE_FOREIGN_TARGET)
    {
        printf("revoke crashed\n");
        return 0;
    }

    printf("revoke survived with exit code %lu\n", code);
    return 0;
}

int main(int argc, char **argv)
{
    setvbuf(stdout, NULL, _IONBF, 0);
    return argc == 4 && !strcmp(argv[1], "revoke") ? revoke(argv[2], argv[3]) : hold(argv[0]);
}
