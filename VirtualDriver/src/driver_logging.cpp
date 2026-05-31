#include <Windows.h>
#include <array>
#include <cstdarg>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <cstdlib>
#include <string>
#include <openvr_driver.h>
#include "driver_logging.h"

namespace
{
    constexpr std::uintmax_t MaxDriverLogFileSizeBytes = 10u * 1024u * 1024u;

    std::filesystem::path ResolveDriverLogFilePath()
    {
        if (const char* localAppData = std::getenv("LOCALAPPDATA"); localAppData && *localAppData)
        {
            return std::filesystem::path(localAppData) / "openvr" / "drivers" / "vrchotas" / "vrchotas-driver.log";
        }

        HMODULE moduleHandle = nullptr;
        if (!GetModuleHandleExW(
                GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                reinterpret_cast<LPCWSTR>(&ResolveDriverLogFilePath),
                &moduleHandle))
        {
            return {};
        }

        std::array<wchar_t, MAX_PATH> modulePath{};
        const auto pathLength = GetModuleFileNameW(moduleHandle, modulePath.data(), static_cast<DWORD>(modulePath.size()));
        if (pathLength == 0 || pathLength >= modulePath.size())
        {
            return {};
        }

        auto path = std::filesystem::path(modulePath.data()).parent_path();
        if (path.empty())
        {
            return {};
        }

        path = path.parent_path();
        if (path.empty())
        {
            return {};
        }

        path = path.parent_path();
        if (path.empty())
        {
            return {};
        }

        auto logDirectory = path.parent_path();
        if (logDirectory.empty())
        {
            return path / "vrchotas-driver.log";
        }

        return logDirectory / "vrchotas-driver.log";
    }

    void TrimDriverLogFileIfNeeded(const std::filesystem::path& logFilePath)
    {
        std::error_code errorCode;
        const auto fileSize = std::filesystem::file_size(logFilePath, errorCode);
        if (errorCode || fileSize <= MaxDriverLogFileSizeBytes)
        {
            return;
        }

        std::ifstream input(logFilePath, std::ios::binary);
        if (!input.is_open())
        {
            return;
        }

        input.seekg(static_cast<std::streamoff>(fileSize - MaxDriverLogFileSizeBytes), std::ios::beg);

        std::string retainedContent(static_cast<std::size_t>(MaxDriverLogFileSizeBytes), '\0');
        input.read(retainedContent.data(), static_cast<std::streamsize>(retainedContent.size()));
        retainedContent.resize(static_cast<std::size_t>(input.gcount()));
        input.close();

        const auto newlinePosition = retainedContent.find('\n');
        if (newlinePosition != std::string::npos && newlinePosition + 1 < retainedContent.size())
        {
            retainedContent.erase(0, newlinePosition + 1);
        }

        std::ofstream output(logFilePath, std::ios::binary | std::ios::trunc);
        if (!output.is_open())
        {
            return;
        }

        output.write(retainedContent.data(), static_cast<std::streamsize>(retainedContent.size()));
    }

    void AppendDriverFileLog(const char* message)
    {
        static std::mutex logMutex;
        static const std::filesystem::path logFilePath = ResolveDriverLogFilePath();

        if (logFilePath.empty())
        {
            return;
        }

        std::error_code errorCode;
        std::filesystem::create_directories(logFilePath.parent_path(), errorCode);

        std::lock_guard<std::mutex> lock(logMutex);
        std::ofstream stream(logFilePath, std::ios::app);
        if (!stream.is_open())
        {
            return;
        }

        SYSTEMTIME localTime{};
        GetLocalTime(&localTime);
        char timestamp[64]{};
        snprintf(
            timestamp,
            sizeof(timestamp),
            "%04u-%02u-%02u %02u:%02u:%02u.%03u",
            localTime.wYear,
            localTime.wMonth,
            localTime.wDay,
            localTime.wHour,
            localTime.wMinute,
            localTime.wSecond,
            localTime.wMilliseconds);

        stream << timestamp << ' ' << message << std::endl;
        stream.close();
        TrimDriverLogFileIfNeeded(logFilePath);
    }
}

void DriverLog(const char* message)
{
    AppendDriverFileLog(message);

    if (!vr::VRDriverContext())
    {
        return;
    }

    if (auto* log = vr::VRDriverLog())
    {
        log->Log(message);
    }
}

void DriverLogF(const char* format, ...)
{
    char buffer[512]{};
    va_list args;
    va_start(args, format);
    vsnprintf(buffer, sizeof(buffer), format, args);
    va_end(args);
    DriverLog(buffer);
}

void DriverLogLastError(const char* action)
{
    DriverLogF("[vrchotas] %s failed. GetLastError=%lu", action, GetLastError());
}
