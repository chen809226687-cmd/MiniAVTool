#include "overlay.h"

// OpenCV 的绘图文字和矩形接口位于 imgproc 模块。
#include <opencv2/imgproc.hpp>

// C 风格本地时间转换函数需要 ctime。
#include <ctime>

// put_time 和 fixed/setprecision 需要 iomanip。
#include <iomanip>

// 使用字符串流组装 FPS 和运动数量等动态文本。
#include <sstream>

namespace miniav::ui {

std::string formatTimestamp(const std::chrono::system_clock::time_point now) {
    // 把 C++ 时间点转换为平台时间类型。
    const auto time = std::chrono::system_clock::to_time_t(now);

    // 初始化为零，避免未初始化字段影响格式化结果。
    std::tm localTime{};
#ifdef _WIN32
    // Windows 版本的 localtime_s 是线程安全接口。
    localtime_s(&localTime, &time);
#else
    // POSIX 平台使用对应的线程安全接口。
    localtime_r(&time, &localTime);
#endif

    // 按年-月-日 时:分:秒格式输出本地时间。
    std::ostringstream output;
    output << std::put_time(&localTime, "%Y-%m-%d %H:%M:%S");
    return output.str();
}

void drawOverlay(cv::Mat& frame, const double fps, const vision::ViewMode mode,
                 const std::string& timestamp, const std::size_t motionCount) {
    // OpenCV 使用 BGR 顺序，因此这里的颜色值不是常见的 RGB 顺序。
    const auto white = cv::Scalar(245, 245, 245);
    const auto cyan = cv::Scalar(255, 210, 0);
    const auto black = cv::Scalar(20, 20, 20);

    // 顶部深色背景保证标题和指标在各种画面上都清晰可见。
    cv::rectangle(frame, cv::Rect(0, 0, frame.cols, 82), black, cv::FILLED);

    // 绘制应用标题。
    cv::putText(frame, "MiniAV Camera Vision", cv::Point(18, 28),
                cv::FONT_HERSHEY_SIMPLEX, 0.72, cyan, 2, cv::LINE_AA);

    // 组装 FPS、实际分辨率和当前模式。
    std::ostringstream metrics;
    metrics << std::fixed << std::setprecision(1) << "FPS: " << fps
            << " | " << frame.cols << "x" << frame.rows
            << " | Mode: " << vision::modeName(mode);

    // 把运行指标绘制到顶部区域。
    cv::putText(frame, metrics.str(), cv::Point(18, 56),
                cv::FONT_HERSHEY_SIMPLEX, 0.58, white, 1, cv::LINE_AA);

    // 在底部绘制时间戳。
    cv::putText(frame, timestamp, cv::Point(18, frame.rows - 42),
                cv::FONT_HERSHEY_SIMPLEX, 0.58, white, 1, cv::LINE_AA);

    // 在底部绘制键盘操作提示。
    cv::putText(frame, "1 Original  2 Gray  3 Edge  4 Binary  5 Motion  S Snapshot  ESC Exit",
                cv::Point(18, frame.rows - 16), cv::FONT_HERSHEY_SIMPLEX, 0.48, white, 1,
                cv::LINE_AA);

    if (mode == vision::ViewMode::Motion) {
        // 只有运动模式需要显示运动区域数量。
        std::ostringstream motionText;
        motionText << "Motion regions: " << motionCount;
        cv::putText(frame, motionText.str(), cv::Point(frame.cols - 220, 56),
                    cv::FONT_HERSHEY_SIMPLEX, 0.55, cv::Scalar(0, 220, 255), 1,
                    cv::LINE_AA);
    }
}

} // namespace miniav::ui
