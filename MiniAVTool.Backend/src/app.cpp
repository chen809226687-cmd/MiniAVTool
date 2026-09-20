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
		// 当参数不足或命令名称不正确时，向用户展示后端支持的命令格式。
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
			// AnalyzeFile 只负责从媒体源读取元数据。
			MediaInfo info;
			std::wstring error;
			if (!AnalyzeFile(inputPath, info, error))
			{
				WriteLine(error);
				return false;
			}

			// 分析结果通过 media_info.json 输出，供 WPF 前端读取。
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
			// 抽帧函数会同时填充媒体元数据和实际抽取数量。
			MediaInfo info;
			std::wstring error;
			if (!ExtractFrames(inputPath, outputDir, count, info, error))
			{
				WriteLine(error);
				return false;
			}

			// 先写帧文件，再写汇总 JSON，保证前端可以知道实际抽取结果。
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
			// 音频提取同样把结果状态写入 MediaInfo。
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
			// “全部执行”是三个独立操作的串行组合：
			// 先分析，再抽帧，最后提取音频。
			// 每一步失败都会立即停止，避免继续产生不完整结果。
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
		// C++ 后端使用宽字符参数，但控制台输出切换为 UTF-8，
		// 这样 WPF 通过 UTF-8 读取标准输出时能够正确显示文本。
		SetConsoleOutputCP(CP_UTF8);
		SetConsoleCP(CP_UTF8);

		if (argc < 4)
		{
			PrintUsage();
			return 1;
		}

		// 命令行约定：
		// argv[1] = 命令，argv[2] = 输入文件，argv[3] = 输出目录，
		// argv[4] = 可选帧数。
		const std::wstring command = argv[1];
		const std::filesystem::path inputPath = argv[2];
		const std::filesystem::path outputDir = argv[3];
		const int count = argc >= 5 ? std::max(0, _wtoi(argv[4])) : 0;

		if (!std::filesystem::exists(inputPath))
		{
			WriteLine(L"Input file does not exist.");
			return 1;
		}

		// Media Foundation 基于 COM，因此必须先初始化当前线程的 COM 环境。
		if (FAILED(CoInitializeEx(nullptr, COINIT_MULTITHREADED)))
		{
			WriteLine(L"CoInitializeEx failed.");
			return 1;
		}

		// 初始化 Media Foundation 全局运行时；结束前必须调用 MFShutdown。
		if (FAILED(MFStartup(MF_VERSION)))
		{
			WriteLine(L"MFStartup failed.");
			CoUninitialize();
			return 1;
		}

		// 根据命令分发到具体的媒体处理函数。
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

		// 与启动顺序相反释放媒体框架和 COM 资源。
		MFShutdown();
		CoUninitialize();
		return ok ? 0 : 1;
	}
} // namespace miniav
