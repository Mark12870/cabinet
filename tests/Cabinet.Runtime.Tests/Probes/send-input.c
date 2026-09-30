#include <stdio.h>
#include <windows.h>
#include <shellapi.h>

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, PWSTR command_line, int show)
{
    int count;
    wchar_t **arguments = CommandLineToArgvW(GetCommandLineW(), &count);
    if (count != 5)
        return 2;

    FILE *input = _wfopen(arguments[1], L"rb");
    HWND window = FindWindowW(NULL, arguments[2]);
    int x = _wtoi(arguments[3]);
    int y = _wtoi(arguments[4]);
    LocalFree(arguments);
    if (input == NULL)
        return 3;

    if (window == NULL)
    {
        fclose(input);
        return 4;
    }
    if (IsIconic(window))
        ShowWindow(window, SW_RESTORE);
    SetForegroundWindow(window);
    for (int attempt = 0; attempt < 20 && GetForegroundWindow() != window; ++attempt)
        Sleep(10);
    if (GetForegroundWindow() != window)
    {
        fclose(input);
        return 5;
    }
    Sleep(50);
    if (GetForegroundWindow() != window)
    {
        fclose(input);
        return 6;
    }

    POINT point = { x, y };
    RECT client;
    if (!GetClientRect(window, &client) || !PtInRect(&client, point) || !ClientToScreen(window, &point))
    {
        fclose(input);
        return 7;
    }
    HWND hit = WindowFromPoint(point);
    if (hit != window && !IsChild(window, hit))
    {
        fclose(input);
        return 8;
    }
    int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
    int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
    int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
    int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
    if (width <= 0 || height <= 0)
    {
        fclose(input);
        return 9;
    }
    INPUT mouse[3] = { 0 };
    for (int event = 0; event < 3; ++event)
        mouse[event].type = INPUT_MOUSE;
    mouse[0].mi.dx = (LONG) (((2LL * (point.x - left) + 1) * 65536) / (2LL * width));
    mouse[0].mi.dy = (LONG) (((2LL * (point.y - top) + 1) * 65536) / (2LL * height));
    mouse[0].mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
    mouse[1].mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
    mouse[2].mi.dwFlags = MOUSEEVENTF_LEFTUP;
    if (SendInput(3, mouse, sizeof(INPUT)) != 3)
    {
        fclose(input);
        return 10;
    }

    WCHAR character;
    while (fread(&character, sizeof(character), 1, input) == 1)
    {
        INPUT events[2] = { 0 };
        events[0].type = INPUT_KEYBOARD;
        events[0].ki.wScan = (WORD) character;
        events[0].ki.dwFlags = KEYEVENTF_UNICODE;
        events[1] = events[0];
        events[1].ki.dwFlags |= KEYEVENTF_KEYUP;
        if (SendInput(2, events, sizeof(INPUT)) != 2)
        {
            fclose(input);
            return 11;
        }
        Sleep(50);
    }

    fclose(input);
    return 0;
}
