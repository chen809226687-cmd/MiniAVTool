#define NOMINMAX
#include "app.h"

#include "media_pipeline.h"
#include "media_utils.h"
#include "media_writer.h"

#include <algorithm>
#include <filesystem>
#include <string>

#include <Windows.h>
#include <mfapi.h>

namespace miniav
{
namespace
{
void PrintUsage()
{
    WriteLine(L"MiniAVTool.Backend usage:");
    WriteLine(L"  MiniAVTool.Backend analyze <input> <outputDir>");
    WriteLine(L"  MiniAVTool.Backend extract-frames <input> <outputDir> [count]");
    WriteLine(L"  MiniAVTool.Backend extract-audio <input> <outputDir>");
    WriteLine(L"  MiniAVTool.Backend all <input> <outputDir> [count]");
}

bool RunAnalyze(const std::filesystem::path& inputPath, const std::filesystem::path& outputDir)
{
    MediaInfo info;
    std::wstring error;
    if (!AnalyzeFile(inputPath, info, error))
    {
        WriteLine(error);
        return false;
    }

    if (!EnsureDirectory(outputDir) || !WriteMediaJson(outputDir, info))
    {
        WriteLine(L"Failed to write media_info.json.");
        return false;
    }

    WriteLine(L"Analyze completed.");
    return true;
}

bool RunFrames(const std::filesystem::path& inputPath, const std::filesystem::path& outputDir, int count)
{
    MediaInfo info;
    std::wstring error;
    if (!ExtractFrames(inputPath, outputDir, count, info, error))
    {
        WriteLine(error);
        return false;
    }

    if (!WriteMediaJson(outputDir, info))
    {
        WriteLine(L"Failed to write media_info.json.");
        return false;
    }

    WriteLine(L"Frame extraction completed.");
    return true;
}

bool RunAudio(const std::filesystem::path& inputPath, const std::filesystem::path& outputDir)
{
    MediaInfo info;
    std::wstring error;
    if (!ExtractAudio(inputPath, outputDir, info, error))
    {
        WriteLine(error);
        return false;
    }

    if (!WriteMediaJson(outputDir, info))
    {
        WriteLine(L"Failed to write media_info.json.");
        return false;
    }

    WriteLine(L"Audio extraction completed.");
    return true;
}

bool RunAll(const std::filesystem::path& inputPath, const std::filesystem::path& outputDir, int count)
{
    MediaInfo info;
    std::wstring error;
    if (!AnalyzeFile(inputPath, info, error))
    {
        WriteLine(error);
        return false;
    }

    if (!ExtractFrames(inputPath, outputDir, count, info, error))
    {
        WriteLine(error);
        return false;
    }

    if (!ExtractAudio(inputPath, outputDir, info, error))
    {
        WriteLine(error);
        return false;
    }

    if (!WriteMediaJson(outputDir, info))
    {
        WriteLine(L"Failed to write media_info.json.");
        return false;
    }

    WriteLine(L"Full pipeline completed.");
    return true;
}
} // namespace

int RunApp(int argc, wchar_t* argv[])
{
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);

    if (argc < 4)
    {
        PrintUsage();
        return 1;
    }

    const std::wstring command = argv[1];
    const std::filesystem::path inputPath = argv[2];
    const std::filesystem::path outputDir = argv[3];
    const int count = argc >= 5 ? std::max(0, _wtoi(argv[4])) : 0;

    if (!std::filesystem::exists(inputPath))
    {
        WriteLine(L"Input file does not exist.");
        return 1;
    }

    if (FAILED(CoInitializeEx(nullptr, COINIT_MULTITHREADED)))
    {
        WriteLine(L"CoInitializeEx failed.");
        return 1;
    }

    if (FAILED(MFStartup(MF_VERSION)))
    {
        WriteLine(L"MFStartup failed.");
        CoUninitialize();
        return 1;
    }

    bool ok = false;
    if (command == L"analyze")
    {
        ok = RunAnalyze(inputPath, outputDir);
    }
    else if (command == L"extract-frames")
    {
        ok = RunFrames(inputPath, outputDir, count);
    }
    else if (command == L"extract-audio")
    {
        ok = RunAudio(inputPath, outputDir);
    }
    else if (command == L"all")
    {
        ok = RunAll(inputPath, outputDir, count);
    }
    else
    {
        PrintUsage();
    }

    MFShutdown();
    CoUninitialize();
    return ok ? 0 : 1;
}
} // namespace miniav
