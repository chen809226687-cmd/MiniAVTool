#include "media_writer.h"

#include "media_utils.h"

#include <fstream>
#include <iomanip>
#include <sstream>

namespace miniav
{
// 将 MediaInfo 手动序列化成前端约定的 JSON 结构。
// 当前项目结构固定且字段数量少，因此使用 wstringstream 生成文本。
std::wstring BuildJson(const MediaInfo& info)
{
    // 使用宽字符串流，避免 Windows 路径中的 UTF-16 字符在拼接时丢失。
    std::wstringstream ss;
    ss << L"{\n";
    // 路径和 codec 是 JSON 字符串，必须经过 JsonEscape。
    ss << L"  \"inputPath\": \"" << JsonEscape(info.inputPath) << L"\",\n";
    ss << L"  \"durationSeconds\": " << std::fixed << std::setprecision(3) << info.durationSeconds << L",\n";
    // 视频信息对象，即使没有视频流也会输出默认值，
    // 这样前端反序列化时结构保持稳定。
    ss << L"  \"video\": {\n";
    ss << L"    \"streamIndex\": " << info.video.streamIndex << L",\n";
    ss << L"    \"codec\": \"" << JsonEscape(info.video.codec) << L"\",\n";
    ss << L"    \"width\": " << info.video.width << L",\n";
    ss << L"    \"height\": " << info.video.height << L",\n";
    ss << L"    \"frameRateNumerator\": " << info.video.frameRateNumerator << L",\n";
    ss << L"    \"frameRateDenominator\": " << info.video.frameRateDenominator << L",\n";
    ss << L"    \"frameRate\": " << std::fixed << std::setprecision(3) << info.video.frameRate << L"\n";
    ss << L"  },\n";
    // 音频信息对象的输出方式与视频相同。
    ss << L"  \"audio\": {\n";
    ss << L"    \"streamIndex\": " << info.audio.streamIndex << L",\n";
    ss << L"    \"codec\": \"" << JsonEscape(info.audio.codec) << L"\",\n";
    ss << L"    \"sampleRate\": " << info.audio.sampleRate << L",\n";
    ss << L"    \"channels\": " << info.audio.channels << L",\n";
    ss << L"    \"bitsPerSample\": " << info.audio.bitsPerSample << L",\n";
    ss << L"    \"avgBytesPerSecond\": " << info.audio.avgBytesPerSecond << L"\n";
    ss << L"  },\n";
    ss << L"  \"extractedFrames\": " << info.extractedFrames << L",\n";
    ss << L"  \"frameCountTarget\": " << info.frameCountTarget << L",\n";
    ss << L"  \"audioExtracted\": " << (info.audioExtracted ? L"true" : L"false") << L"\n";
    ss << L"}\n";
    // 返回完整 JSON 文本，末尾保留换行便于人工查看。
    return ss.str();
}

// 在输出目录中创建或覆盖 media_info.json。
bool WriteMediaJson(const std::filesystem::path& outputDir, const MediaInfo& info)
{
    // 所有处理命令都使用统一文件名，前端因此不需要知道具体命令。
    const std::filesystem::path jsonPath = outputDir / L"media_info.json";

    // 以二进制方式打开，随后写入 UTF-8 字节。
    std::ofstream out(jsonPath, std::ios::binary);
    if (!out)
    {
        return false;
    }

    // 先生成 UTF-16 JSON，再统一转换成 UTF-8。
    const std::string utf8 = ToUtf8(BuildJson(info));

    // 将 UTF-8 字节写入磁盘。
    out.write(utf8.data(), static_cast<std::streamsize>(utf8.size()));
    return true;
}
} // namespace miniav
