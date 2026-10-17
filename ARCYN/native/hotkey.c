#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <wchar.h>
#include <wctype.h>
#include <shellapi.h>

static int parse_hotkey(const wchar_t *text, UINT *modifiers, UINT *key) {
    wchar_t copy[128], *context = NULL, *part;
    if (wcslen(text) >= 128) return 0;
    if (!*text || *text == L'+' || text[wcslen(text)-1] == L'+' || wcsstr(text,L"++")) return 0;
    wcscpy_s(copy, 128, text); *modifiers = MOD_NOREPEAT; *key = 0;
    for (part = wcstok_s(copy, L"+", &context); part; part = wcstok_s(NULL, L"+", &context)) {
        UINT modifier = !_wcsicmp(part,L"ALT") ? MOD_ALT : !_wcsicmp(part,L"SHIFT") ? MOD_SHIFT : !_wcsicmp(part,L"CTRL") ? MOD_CONTROL : 0;
        if (*key) return 0;
        if (modifier) { if (*modifiers & modifier) return 0; *modifiers |= modifier; }
        else if (wcslen(part) == 1 && ((part[0] >= L'A' && part[0] <= L'Z') || (part[0] >= L'a' && part[0] <= L'z') || (part[0] >= L'0' && part[0] <= L'9'))) *key = (UINT)towupper(part[0]);
        else return 0; /* Win combinations and debugger-reserved F12 are intentionally unsupported. */
    }
    return *key && (*modifiers & (MOD_CONTROL | MOD_ALT));
}
static int launch(void) {
    wchar_t path[32768], command[32772]; STARTUPINFOW si = {sizeof(si)}; PROCESS_INFORMATION pi;
    if (!GetModuleFileNameW(NULL, path, 32768)) return 0;
    wchar_t *slash = wcsrchr(path,L'\\'); if (!slash) return 0;
    wcscpy_s(slash + 1, 32768 - (size_t)(slash + 1 - path), L"ARCYN.exe");
    if (swprintf_s(command,32772,L"\"%s\"",path) < 0) return 0;
    *slash = 0;
    if (!CreateProcessW(NULL,command,NULL,NULL,FALSE,CREATE_SUSPENDED,NULL,path,&si,&pi)) return 0;
    AllowSetForegroundWindow(pi.dwProcessId);
    if (ResumeThread(pi.hThread) == (DWORD)-1) { TerminateProcess(pi.hProcess,1); CloseHandle(pi.hThread); CloseHandle(pi.hProcess); return 0; }
    CloseHandle(pi.hThread); CloseHandle(pi.hProcess); return 1;
}
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, PWSTR tail, int show) {
    (void)instance; (void)previous; (void)tail; (void)show;
    int count; wchar_t **args = CommandLineToArgvW(GetCommandLineW(), &count);
    UINT modifiers = MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, key = L'D';
    if (!args) return 1;
    if (count == 2 && !wcscmp(args[1],L"--self-test")) {
        UINT m,k; int ok = parse_hotkey(L"ALT+SHIFT+D",&m,&k) && k == L'D' && m == (MOD_ALT | MOD_SHIFT | MOD_NOREPEAT) &&
            parse_hotkey(L"alt+shift+d",&m,&k) && k == L'D' && parse_hotkey(L"CTRL+7",&m,&k) && k == L'7' &&
            !parse_hotkey(L"WIN+L",&m,&k) && !parse_hotkey(L"ALT+F12",&m,&k) && !parse_hotkey(L"D",&m,&k) &&
            !parse_hotkey(L"ALT++D",&m,&k) && !parse_hotkey(L"+ALT+D",&m,&k) && !parse_hotkey(L"ALT+D+",&m,&k) &&
            !parse_hotkey(L"D+ALT",&m,&k) && !parse_hotkey(L"ALT+ALT+D",&m,&k) && !parse_hotkey(L"SHIFT+D",&m,&k);
        LocalFree(args); return ok ? 0 : 1;
    }
    wchar_t user[256], event_name[320]; DWORD length = 256;
    if (!GetUserNameW(user,&length)) { LocalFree(args); return 1; }
    swprintf_s(event_name,320,L"Local\\ARCYN.Hotkey.%s",user);
    if (count == 2 && !wcscmp(args[1],L"--stop")) {
        HANDLE stop = OpenEventW(EVENT_MODIFY_STATE,FALSE,event_name);
        if (stop) { SetEvent(stop); CloseHandle(stop); } LocalFree(args); return 0;
    }
    if (count != 1 && !(count == 3 && !wcscmp(args[1],L"--hotkey") && parse_hotkey(args[2],&modifiers,&key))) {
        MessageBoxW(NULL,L"Use --hotkey ALT+SHIFT+D (Ctrl/Alt with A-Z or 0-9). Win shortcuts and F12 are reserved.",L"ARCYN hotkey",MB_ICONERROR); LocalFree(args); return 2;
    }
    LocalFree(args);
    HANDLE stop = CreateEventW(NULL,TRUE,FALSE,event_name);
    if (!stop) return 1;
    if (GetLastError() == ERROR_ALREADY_EXISTS) { CloseHandle(stop); return 0; }
    if (!RegisterHotKey(NULL,1,modifiers,key)) {
        MessageBoxW(NULL,L"The shortcut is already in use or unavailable. Choose another shortcut with --hotkey.",L"ARCYN hotkey",MB_ICONERROR);
        CloseHandle(stop); return 3;
    }
    int result = 0; MSG message;
    for (;;) {
        DWORD wait = MsgWaitForMultipleObjects(1,&stop,FALSE,INFINITE,QS_ALLINPUT);
        if (wait == WAIT_OBJECT_0) break;
        if (wait == WAIT_FAILED) { result = 1; break; }
        while (PeekMessageW(&message,NULL,0,0,PM_REMOVE)) {
            if (message.message == WM_QUIT) goto done;
            if (message.message == WM_HOTKEY && !launch()) MessageBoxW(NULL,L"ARCYN.exe could not be started. Keep the helper beside it.",L"ARCYN hotkey",MB_ICONERROR);
        }
    }
done:
    UnregisterHotKey(NULL,1); CloseHandle(stop); return result;
}
