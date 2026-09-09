#include "media_writer.h"

#include "media_utils.h"

#include <fstream>
#include <iomanip>
#include <sstream>

namespace miniav
{
std::wstring BuildJson(const MediaInfo& info)
{
    std::wstringstream ss;
    ss << L"{\n";
    ss << L"  \"inputPath\": \"" << JsonEscape(info.inputPath) << L"\",\n";
    ss << L"  \"durationSeconds\": " << std::fixed << std::setprecision(3) << info.durationSeconds << L",\n";
    ss << L"  \"video\": {\n";
    ss << L"    \"streamIndex\": " << info.video.streamIndex << L",\n";
    ss << L"    \"codec\": \"" << JsonEscape(info.video.codec) << L"\",\n";
    ss << L"    \"width\": " << info.video.width << L",\n";
    ss << L"    \"height\": " << info.video.height << L",\n";
    ss << L"    \"frameRateNumerator\": " << info.video.frameRateNumerator << L",\n";
    ss << L"    \"frameRateDenominator\": " << info.video.frameRateDenominator << L",\n";
    ss << L"    \"frameRate\": " << std::fixed << std::setprecision(3) << info.video.frameRate << L"\n";
    ss << L"  },\n";
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
    return ss.str();
}

bool WriteMediaJson(const std::filesystem::path& outputDir, const MediaInfo& info)
{
    const std::filesystem::path jsonPath = outputDir / L"media_info.json";
    std::ofstream out(jsonPath, std::ios::binary);
    if (!out)
    {
        return false;
    }

    const std::string utf8 = ToUtf8(BuildJson(info));
    out.write(utf8.data(), static_cast<std::streamsize>(utf8.size()));
    return true;
}
} // namespace miniav
