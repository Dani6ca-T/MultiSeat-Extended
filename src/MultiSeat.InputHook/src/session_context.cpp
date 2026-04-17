#include "session_context.h"

DWORD GetCurrentSessionId()
{
    DWORD sessionId = 0;
    ProcessIdToSessionId(GetCurrentProcessId(), &sessionId);
    return sessionId;
}

DWORD GetSessionIdForProcess(DWORD processId)
{
    DWORD sessionId = 0xFFFFFFFF;
    if (!ProcessIdToSessionId(processId, &sessionId))
    {
        return 0xFFFFFFFF;
    }
    return sessionId;
}
