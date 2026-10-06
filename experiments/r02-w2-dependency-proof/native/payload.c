#include <windows.h>

__declspec(dllexport) int W2Observe(void)
{
#ifdef UNDECLARED
    return 22;
#else
    return 11;
#endif
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)instance;
    (void)reason;
    (void)reserved;
    return TRUE;
}
