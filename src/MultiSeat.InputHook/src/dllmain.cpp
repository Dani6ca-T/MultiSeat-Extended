/// @file dllmain.cpp
/// @brief DLL entry point for MultiSeatInputHook.

#include <windows.h>

// Forward declaration from input_hook.cpp
void SetModuleHandle(HINSTANCE hModule);

BOOL APIENTRY DllMain(HINSTANCE hModule, DWORD reason, LPVOID reserved)
{
    switch (reason)
    {
    case DLL_PROCESS_ATTACH:
        DisableThreadLibraryCalls(hModule);
        SetModuleHandle(hModule);
        break;

    case DLL_PROCESS_DETACH:
        // Clean up hooks if the DLL is being unloaded
        // (the exported MultiSeatHook_Uninstall should be called first,
        //  but this is a safety net)
        break;
    }

    return TRUE;
}
