// Loader for CloudRedirect under OpenSteamTool.
//
// Installed as `cr_loader.dll` in the Steam folder. OST's xinput1_4.dll and
// dwmapi.dll have their `"OpenSteamTool.dll"` string in .rdata replaced with
// `"cr_loader.dll"`, so OST's existing `LoadLibraryA(...)` call resolves to
// this stub instead. The real `OpenSteamTool.dll` stays at its natural
// filename, untouched; the stub loads it (which runs OST's real DllMain)
// and then loads `cloud_redirect.dll`.
//
// OST has zero exports (verified via IDA: only DllEntryPoint and a TLS
// callback), so no export forwarding is required. The stub just needs to
// return a non-NULL HMODULE so OST's `LoadLibraryA(...) != NULL` check passes.

#include <windows.h>
#include <string.h>

static const char kRealOstName[] = "OpenSteamTool.dll";
static const char kCloudRedirectName[] = "cloud_redirect.dll";

static BOOL LoadSiblings(HMODULE selfModule) {
    char selfPath[MAX_PATH];
    DWORD n = GetModuleFileNameA(selfModule, selfPath, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) return FALSE;

    // Truncate to "<dir>\" (keep trailing backslash for concatenation).
    char* lastSlash = strrchr(selfPath, '\\');
    if (!lastSlash) return FALSE;
    *(lastSlash + 1) = '\0';
    size_t dirLen = (size_t)(lastSlash + 1 - selfPath);
    if (dirLen + sizeof(kRealOstName) > MAX_PATH ||
        dirLen + sizeof(kCloudRedirectName) > MAX_PATH) return FALSE;

    char ostPath[MAX_PATH];
    memcpy(ostPath, selfPath, dirLen);
    memcpy(ostPath + dirLen, kRealOstName, sizeof(kRealOstName));
    HMODULE hOst = LoadLibraryA(ostPath);
    if (!hOst) return FALSE;

    char crPath[MAX_PATH];
    memcpy(crPath, selfPath, dirLen);
    memcpy(crPath + dirLen, kCloudRedirectName, sizeof(kCloudRedirectName));
    LoadLibraryA(crPath); // optional; CR may not be deployed

    return TRUE;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID) {
    switch (reason) {
    case DLL_PROCESS_ATTACH:
        DisableThreadLibraryCalls(hModule);
        return LoadSiblings(hModule);
    }
    return TRUE;
}
