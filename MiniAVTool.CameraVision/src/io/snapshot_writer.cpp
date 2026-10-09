#include "snapshot_writer.h"

// imwrite 用于把 OpenCV 图像编码并写入 JPG 文件。
#include <opencv2/imgcodecs.hpp>

// 使用当前时间生成基本唯一的截图文件名。
#include <chrono>

namespace miniav::io {

bool saveSnapshot(const cv::Mat& frame, const std::filesystem::path& directory,
                  std::filesystem::path& savedPath, std::string& error) {
    // 使用 error_code 版本，避免目录创建失败时直接抛出 filesystem 异常。
    std::error_code filesystemError;
    std::filesystem::create_directories(directory, filesystemError);
    if (filesystemError) {
        error = "Cannot create capture directory: " + filesystemError.message();
        return false;
    }

    // 使用 Unix epoch 毫秒作为文件名的一部分，降低连续截图重名的概率。
    const auto milliseconds = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::system_clock::now().time_since_epoch()).count();
    savedPath = directory / ("frame_" + std::to_string(milliseconds) + ".jpg");

    try {
        // imwrite 根据文件扩展名选择编码器，这里扩展名决定使用 JPG。
        if (!cv::imwrite(savedPath.string(), frame)) {
            error = "OpenCV could not write the image.";
            return false;
        }
    } catch (const cv::Exception& exception) {
        // OpenCV 编码器或文件系统错误统一转成文本交给 main.cpp 显示。
        error = exception.what();
        return false;
    }

    return true;
}

} // namespace miniav::io
